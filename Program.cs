using static System.Console;
using MessengerModel;
using System.Runtime.Serialization.Json;
using System.Net.Sockets;
using System.Net;
using CommandDLL;
using System.Text;
using System.Threading.Channels;
using System.Collections.Generic;



namespace ServerAPP
{
    internal class Server
    {
        ServerResponse response = new ServerResponse();

        static void Main(string[] args)
        {
            Server s = new Server();
            s.Accept();
            Read();
        }
        private async void Accept()
        {
            await Task.Run(async () =>
            {
                try
                {
                    // TcpListener ожидает подключения от TCP-клиентов сети.
                    TcpListener listener = new TcpListener(
                    IPAddress.Any /* Предоставляет IP-адрес, указывающий, что сервер должен контролировать действия клиентов на всех сетевых интерфейсах.*/,
                    49152 /* порт */);
                    listener.Start(); // Запускаем ожидание входящих запросов на подключение
                    while (true)
                    {
                        // Принимаем ожидающий запрос на подключение 
                        // Метод AcceptTcpClient — это блокирующий метод, возвращающий объект TcpClient, 
                        // который может использоваться для приема и передачи данных.
                        TcpClient client = await listener.AcceptTcpClientAsync();
                        Receive(client);
                    }
                }
                catch (Exception ex)
                {
                    WriteLine("Сервер: " + ex.Message);
                }
            });
        }
        private async void Receive(TcpClient tcpClient)
        {
            await Task.Run(async () =>
            {
                NetworkStream netstream = null;

                try
                {
                    // Получим объект NetworkStream, используемый для приема и передачи данных.
                    netstream = tcpClient.GetStream();
                    byte[] arr = new byte[tcpClient.ReceiveBufferSize /* размер приемного буфера */];
                    // Читаем данные из объекта NetworkStream.
                    while (true)
                    {
                        int len = await netstream.ReadAsync(arr, 0, tcpClient.ReceiveBufferSize);// Возвращает фактически считанное число байтов

                        if (len == 0)
                        {
                            netstream.Close();
                            tcpClient.Close(); // закрываем TCP-подключение и освобождаем все ресурсы, связанные с объектом TcpClient.
                            return;
                        }
                        // Создадим поток, резервным хранилищем которого является память.
                        byte[] copy = new byte[len];
                        Array.Copy(arr, 0, copy, 0, len);
                        MemoryStream stream = new MemoryStream(copy);
                        var jsonFormatter = new DataContractJsonSerializer(typeof(Wrapper));
                        Wrapper wr = jsonFormatter.ReadObject(stream) as Wrapper;// выполняем десериализацию
                        User us = new User();
                        us = wr.user;

                        string IP = tcpClient.Client.RemoteEndPoint.ToString();// информация об удаленном хосте, который отправил датаграмму
                        string IPRedact = IP.Substring(0,13);
                        us.IPadress = IPRedact;

                        switch (wr.commands)
                        {
                            case Wrapper.Commands.Registratioin:
                                RegistratioinUser(us, netstream);
                                break;
                            case Wrapper.Commands.Authorization:
                                AuthorizationUser(us, netstream);
                                break;
                            case Wrapper.Commands.Redact:
                                RedactUser(wr.NewPassword, us, netstream);
                                break;
                            case Wrapper.Commands.Remove:
                                RemoveUser(us, netstream);
                                break;
                        }
                        stream.Close();
                    }
                }
                catch (Exception ex)
                {
                    WriteLine("Сервер: " + ex.Message);
                }
                finally
                {
                    netstream?.Close();
                    tcpClient?.Close(); // закрываем TCP-подключение и освобождаем все ресурсы, связанные с объектом TcpClient.
                }
            });
        }

        private void RegistratioinUser(User us, NetworkStream netstream)
        {
            try
            {
                //полученную от клиента информацию добавляем в BD
                using (var db = new MessengerContext())
                {
                    var query = from b in db.Users
                                where b.IPadress == us.IPadress
                                select b;
                    string theReply = null;
                    if (query.Count() != 0)
                    {
                        theReply = "Такой пользователь уже зарегистрирован!";
                        response.command = theReply;                       
                        WriteLine(us.Nick + " " + us.IPadress + " " + theReply);
                        MemoryStream stream = new MemoryStream();
                        var jsonFormatter = new DataContractJsonSerializer(typeof(ServerResponse));
                        jsonFormatter.WriteObject(stream, response);
                        byte[] arr = stream.ToArray(); // записываем содержимое потока в байтовый массив
                        stream.Close();
                        netstream.Write(arr, 0, arr.Length); // записываем данные в NetworkStream.
                    }
                    else
                    {
                        db.Users.Add(us);
                        db.SaveChanges();
                        theReply = "Пользователь успешно зарегистрирован!"; // для вывода в консоль сервера
                        WriteLine(us.Nick + " " + us.IPadress + " " + theReply);

                        SendCollection(us, netstream);
                    }
                }
            }
            catch (Exception ex)
            {
                WriteLine("Сервер: " + ex.Message);
            }
        }
        private void SendCollection(User us, NetworkStream netstream)
        {
            try
            {
                using (var db = new MessengerContext())
                {
                    var query_to_send = from b in db.Users
                                            select b;

                    List<User> listuser = new List<User>();

                    foreach (var b in query_to_send)
                    {
                        User user = new User();
                        user.Nick = b.Nick;
                        user.Password = b.Password;
                        user.IPadress = b.IPadress;
                        user.Avatar = b.Avatar;
                        listuser.Add(user);
                    }
                    response.list = listuser;

                    MemoryStream stream = new MemoryStream();
                    var jsonFormatter = new DataContractJsonSerializer(typeof(ServerResponse));
                    jsonFormatter.WriteObject(stream, response);
                    byte[] arr = stream.ToArray(); // записываем содержимое потока в байтовый массив
                    stream.Close();
                    netstream.Write(arr, 0, arr.Length); // записываем данные в NetworkStream.
                }
            }
            catch (Exception ex)
            {
                WriteLine("Сервер: " + ex.Message);
            }
        }
        private void AuthorizationUser(User us, NetworkStream netstream)
        {
            try
            {
                //проверяем есть ли такой пользователь в BD
                using (var db = new MessengerContext())
                {
                    var query = from b in db.Users
                                 where b.IPadress == us.IPadress
                                 select b;
                    User user = new User();

                    foreach (var b in query)
                    {
                        user = b;
                    }                    

                    if (query != null)
                    {
                        if (user.Nick == us.Nick && user.Password == us.Password)
                        {
                            string theReply = "Пользователь авторизирован!"; // для вывода в консоль сервера
                            WriteLine(us.Nick + " " + us.IPadress + " " + theReply);

                            SendCollection(us, netstream);
                        }
                        else
                        {
                            string theReply = "Введены некорректные данные!";
                            response.command = theReply;
                            response.list = null;
                            WriteLine(us.Nick + " " + us.IPadress + " " + theReply);
                            MemoryStream stream = new MemoryStream();
                            var jsonFormatter = new DataContractJsonSerializer(typeof(ServerResponse));
                            jsonFormatter.WriteObject(stream, response);
                            byte[] arr = stream.ToArray(); // записываем содержимое потока в байтовый массив
                            stream.Close();
                            netstream.Write(arr, 0, arr.Length); // записываем данные в NetworkStream.              

                        }
                    }
                    else
                    {
                        string theReply = "Такой пользователь не зарегистрирован!";
                        response.command = theReply;
                        WriteLine(us.Nick + " " + us.IPadress + " " + theReply);
                        MemoryStream stream = new MemoryStream();
                        var jsonFormatter = new DataContractJsonSerializer(typeof(ServerResponse));
                        jsonFormatter.WriteObject(stream, response);
                        byte[] arr = stream.ToArray(); // записываем содержимое потока в байтовый массив
                        stream.Close();
                        netstream.Write(arr, 0, arr.Length); // записываем данные в NetworkStream.                        
                    }
                }
            }
            catch (Exception ex)
            {
                WriteLine("Сервер: " + ex.Message);
            }
        } 

            private void RedactUser(String NewPassword, User us, NetworkStream netstream)
            {
            try
            {
                //редактируем пользователя в BD
                using (var db = new MessengerContext())
                {
                    var us_redact = (from b in db.Users
                                 where b.IPadress == us.IPadress 
                                 select b).Single();
                    if (us_redact.Password == us.Password) // проверка старого пароля в БД
                    {
                        // редактирование
                        us_redact.Nick = us.Nick;
                        us_redact.Password = NewPassword;
                        us_redact.IPadress = us.IPadress;
                        us_redact.Avatar = us.Avatar;
                        db.SaveChanges();
                        SendCollection(us, netstream);
                    }
                    else
                    {
                        //если старый пароль не совпадает
                        string theReply ="Некорректный пароль!";
                        response.command = theReply;
                        WriteLine(us.Nick + " " + us.IPadress + " " + theReply);
                        MemoryStream stream = new MemoryStream();
                        var jsonFormatter = new DataContractJsonSerializer(typeof(ServerResponse));
                        jsonFormatter.WriteObject(stream, response);
                        byte[] arr = stream.ToArray(); // записываем содержимое потока в байтовый массив
                        stream.Close();
                        netstream.Write(arr, 0, arr.Length); // записываем данные в NetworkStream.
                    }
                }
            }
            catch (Exception ex)
            {
                WriteLine("Сервер: " + ex.Message);
            }
        }
        private void RemoveUser(User us, NetworkStream netstream)
        {
            try
            {
                //удаляем пользователя из BD
                using (var db = new MessengerContext())
                {
                    var us_remove = (from b in db.Users
                                     where b.IPadress == us.IPadress
                                     select b).Single();

                    db.Remove(us_remove);
                    db.SaveChanges();

                    string theReply = "Пользователь успешно удален!";
                    response.command = theReply;
                    response.list = null;
                    WriteLine(us.Nick + " " + us.IPadress + " " + theReply);
                    MemoryStream stream = new MemoryStream();
                    var jsonFormatter = new DataContractJsonSerializer(typeof(ServerResponse));
                    jsonFormatter.WriteObject(stream, response);
                    byte[] arr = stream.ToArray(); // записываем содержимое потока в байтовый массив
                    stream.Close();
                    netstream.Write(arr, 0, arr.Length); // записываем данные в NetworkStream.                                      
                }
            }
            catch (Exception ex)
            {
                WriteLine("Сервер: " + ex.Message);
            }
        }
    }
}