using static System.Console;
using MessengerModel;
using System.Runtime.Serialization.Json;
using System.Net.Sockets;
using System.Net;
using CommandDLL;
using System.Text;
using System.Threading.Channels;



namespace ServerAPP
{
    internal class Server
    {
        NetworkStream netstream;
        TcpClient client;
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
                        client = await listener.AcceptTcpClientAsync();
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
                try
                {
                    // Получим объект NetworkStream, используемый для приема и передачи данных.
                    netstream = tcpClient.GetStream();
                    byte[] arr = new byte[tcpClient.ReceiveBufferSize /* размер приемного буфера */];
                    // Читаем данные из объекта NetworkStream.
                    int len = await netstream.ReadAsync(arr, 0, tcpClient.ReceiveBufferSize);// Возвращает фактически считанное число байтов
                    while (true)
                    {
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
                        wr.user.IPadress = tcpClient.Client.RemoteEndPoint.ToString();// информация об удаленном хосте, который отправил датаграмму
                        switch (wr.commands)
                        {
                            case Wrapper.Commands.Registratioin:
                                RegistratioinUser(wr.user);
                                break;
                            case Wrapper.Commands.Authorization:
                                //AuthorizationUser(wr.user);
                                break;
                            case Wrapper.Commands.Redact:
                                //RedactUser(wr.user);
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

        private async void RegistratioinUser(User us)
        {
            await Task.Run(async () =>
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
                        if (query != null)
                        {
                            theReply = "Такой пользователь уже зарегистрирован!";
                            byte[] msg = Encoding.Default.GetBytes(theReply); // конвертируем строку в массив байтов
                            await netstream.WriteAsync(msg, 0, msg.Length); // записываем данные в NetworkStream.
                            WriteLine(us.Nick + " " + us.IPadress + " " + theReply);
                        }
                        else
                        {
                            db.Users.Add(us);
                            db.SaveChanges();
                            theReply = "Пользователь успешно зарегистрирован!"; // для вывода в консоль сервера
                            WriteLine(us.Nick + " " + us.IPadress + " " + theReply);

                            SendCollection();
                        }
                    }
                }
                catch (Exception ex)
                {
                    WriteLine("Сервер: " + ex.Message);
                }
            });
        }
        private async void SendCollection()
        {
            await Task.Run(async () =>
            {
                try
                {
                    using (var db = new MessengerContext())
                    {
                        var query_to_send = from b in db.Users
                                            select b;
                        MemoryStream stream = new MemoryStream();
                        var jsonFormatter = new DataContractJsonSerializer(typeof(List<User>));
                        jsonFormatter.WriteObject(stream, query_to_send.ToList());
                        byte[] arr = stream.ToArray(); // записываем содержимое потока в байтовый массив
                        stream.Close();
                        await netstream.WriteAsync(arr, 0, arr.Length); // записываем данные в NetworkStream.
                    }
                }
                catch (Exception ex)
                {
                    WriteLine("Сервер: " + ex.Message);
                }
            });
        }
        private async void AuthorizationUser(User us)
        {

        }
        private async void RedactUser(User us)
        {

        }
    }
}