﻿using static System.Console;
using MessengerModel;
using System.Runtime.Serialization.Json;
using System.Net.Sockets;
using System.Net;
using CommandDLL;
using System.Text;
using System.Threading.Channels;
using System.Collections.Generic;
using System.Net.Http;
using Azure;
using MessengerPigeon.Command;
using System;



namespace ServerAPP
{
    internal class Server
    {
        ServerResponse response = new ServerResponse();
        List<NetworkStream> clients = new List<NetworkStream>();
        List<NetworkStream> tcpClients = new List<NetworkStream>();
        static void Main(string[] args)
        {
            Server s = new Server();
            s.Accept();
            s.AcceptMessender();
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
                    WriteLine("Server: " + ex.Message);
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
                    byte[] arr = new byte[100000000 /* размер приемного буфера */];
                    // Читаем данные из объекта NetworkStream.
                    while (true)
                    {
                        int len = await netstream.ReadAsync(arr, 0, arr.Length);// Возвращает фактически считанное число байтов

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
                        string IPRedact = IP.Substring(0, IP.IndexOf(":"));
                        us.IPadress = IPRedact;

                        switch (wr.commands)
                        {
                            case Wrapper.Commands.Registratioin:
                                if (!RegistratioinUser(us, netstream, tcpClient))
                                {
                                    netstream?.Close();
                                    tcpClient.Close();
                                    return;
                                }
                                break;
                            case Wrapper.Commands.Authorization:
                                if(!AuthorizationUser(us, netstream, tcpClient))
                                {
                                    netstream?.Close();
                                    tcpClient.Close();
                                    return;
                                }
                                break;
                            case Wrapper.Commands.Redact:
                                RedactUser(wr.NewPassword, us, netstream);
                                break;
                            case Wrapper.Commands.Remove:
                                RemoveUser(us, netstream);
                                break;
                            case Wrapper.Commands.ExitOnline:
                                ExitOnline(netstream, us, tcpClient);
                                netstream?.Close();
                                tcpClient.Close();
                                return;
                            case Wrapper.Commands.Exit:
                                Exit(netstream, us, tcpClient);
                                netstream?.Close();
                                tcpClient.Close();
                                return;

                        }
                        stream.Close();
                    }
                }
                catch (Exception ex)
                {
                    WriteLine("Server: " + ex.Message);
                }
                //finally
                //{
                //    netstream?.Close();
                //    tcpClient?.Close(); // закрываем TCP-подключение и освобождаем все ресурсы, связанные с объектом TcpClient.
                //}
            });
        }

        private bool RegistratioinUser(User us, NetworkStream netstream, TcpClient tcpClient)
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
                        theReply = "This user is already registered!";
                        response.command = theReply;
                        WriteLine(us.Nick + " " + us.IPadress + " " + theReply);
                        MemoryStream stream = new MemoryStream();
                        var jsonFormatter = new DataContractJsonSerializer(typeof(ServerResponse));
                        jsonFormatter.WriteObject(stream, response);
                        byte[] arr = stream.ToArray(); // записываем содержимое потока в байтовый массив
                        stream.Close();
                        netstream.Write(arr, 0, arr.Length); // записываем данные в NetworkStream.
                        //netstream?.Close();
                        //tcpClient?.Close();
                        return false;
                    }
                    else
                    {
                        db.Users.Add(us);
                        db.SaveChanges();
                        theReply = "User has been successfully registered!"; // для вывода в консоль сервера
                        WriteLine(us.Nick + " " + us.IPadress + " " + theReply);
                        clients.Add(netstream);
                        SendCollection();
                        return true;
                    }
                }
            }
            catch (Exception ex)
            {
                WriteLine("Server: " + ex.Message);
            }
            return true;
        }
        private void SendCollection()
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
                        user.Id = b.Id;
                        user.Nick = b.Nick;
                        user.Phone = b.Phone;
                        user.Password = b.Password;
                        user.IPadress = b.IPadress;
                        user.Avatar = b.Avatar;
                        user.Online = b.Online;
                        listuser.Add(user);
                    }
                    response.list = listuser;                    

                    MemoryStream stream = new MemoryStream();
                    var jsonFormatter = new DataContractJsonSerializer(typeof(ServerResponse));
                    jsonFormatter.WriteObject(stream, response);
                    byte[] arr = stream.ToArray(); // записываем содержимое потока в байтовый массив
                    stream.Close();
                    foreach (var u in clients) //отправка всем пользователям
                    {
                        u.Write(arr, 0, arr.Length);
                    }
                }
            }
            catch (Exception ex)
            {
                WriteLine("Server: " + ex.Message);
            }
        }
        private bool AuthorizationUser(User us, NetworkStream netstream, TcpClient tcpClient)
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
                            string theReply = "User has been successfully authorized!"; // для вывода в консоль сервера
                            var us_online = (from b in db.Users
                                           where b.IPadress == us.IPadress
                                           select b).Single();
                            // редактирование
                            us_online.Online = us.Online;
                            db.SaveChanges();
                            WriteLine(us.Nick + " " + us.IPadress + " " + theReply);
                            clients.Add(netstream);
                            SendCollection();
                            return true;
                        }
                        else
                        {
                            string theReply = "Incorrect data entered!";
                            response.command = theReply;
                            response.list = null;
                            WriteLine(us.Nick + " " + us.IPadress + " " + theReply);
                            MemoryStream stream = new MemoryStream();
                            var jsonFormatter = new DataContractJsonSerializer(typeof(ServerResponse));
                            jsonFormatter.WriteObject(stream, response);
                            byte[] arr = stream.ToArray(); // записываем содержимое потока в байтовый массив
                            stream.Close();
                            netstream.Write(arr, 0, arr.Length); // записываем данные в NetworkStream.
                            //netstream?.Close();
                            //tcpClient?.Close();
                            return false;
                        }
                    }
                    else
                    {
                        string theReply = "This user is not registered!";
                        response.command = theReply;
                        WriteLine(us.Nick + " " + us.IPadress + " " + theReply);
                        MemoryStream stream = new MemoryStream();
                        var jsonFormatter = new DataContractJsonSerializer(typeof(ServerResponse));
                        jsonFormatter.WriteObject(stream, response);
                        byte[] arr = stream.ToArray(); // записываем содержимое потока в байтовый массив
                        stream.Close();
                        netstream.Write(arr, 0, arr.Length); // записываем данные в NetworkStream.
                        //netstream?.Close();
                        //tcpClient?.Close();
                        return false;
                    }
                }
            }
            catch (Exception ex)
            {
                WriteLine("Server: " + ex.Message);
            }
            return true;
        }

        private void RedactUser(string NewPassword, User us, NetworkStream netstream)
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
                        SendCollection();
                    }
                    else
                    {
                        //если старый пароль не совпадает
                        string theReply = "Incorrect password!";
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
                WriteLine("Server: " + ex.Message);
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

                    string theReply = "User successfully deleted!";
                    response.command = theReply;
                    response.list = null;
                    WriteLine(us.Nick + " " + us.IPadress + " " + theReply);
                    MemoryStream stream = new MemoryStream();
                    var jsonFormatter = new DataContractJsonSerializer(typeof(ServerResponse));
                    jsonFormatter.WriteObject(stream, response);
                    byte[] arr = stream.ToArray(); // записываем содержимое потока в байтовый массив
                    stream.Close();
                    netstream.Write(arr, 0, arr.Length); // записываем данные в NetworkStream.
                    clients.Remove(netstream);
                    SendCollection();
                }
            }
            catch (Exception ex)
            {
                WriteLine("Server: " + ex.Message);
            }
        }
        // Реализация методов обменна сообщениями 
        private async void AcceptMessender()
        {
            await Task.Run(async () =>
            {
                try
                {
                    // TcpListener ожидает подключения от TCP-клиентов сети.
                    TcpListener lis = new TcpListener(
                    IPAddress.Any /* Предоставляет IP-адрес, указывающий, что сервер должен контролировать действия клиентов на всех сетевых интерфейсах.*/,
                    49153 /* порт */);
                    lis.Start(); // Запускаем ожидание входящих запросов на подключение
                    while (true)
                    {
                        // Принимаем ожидающий запрос на подключение 
                        // Метод AcceptTcpClient — это блокирующий метод, возвращающий объект TcpClient, 
                        // который может использоваться для приема и передачи данных.
                        TcpClient client = await lis.AcceptTcpClientAsync();
                        ReceiveMessender(client);
                    }
                }
                catch (Exception ex)
                {
                    WriteLine("Server: " + ex.Message);
                }
            });
        }
        private async void ReceiveMessender(TcpClient tcpClient)
        {
            await Task.Run(async () =>
            {
                NetworkStream netstream = null;

                try
                {
                    // Получим объект NetworkStream, используемый для приема и передачи данных.
                    netstream = tcpClient.GetStream();
                    tcpClients.Add(netstream);
                    byte[] arr = new byte[100000000/* размер приемного буфера */];
                    // Читаем данные из объекта NetworkStream.
                    while (true)
                    {
                        int len = await netstream.ReadAsync(arr, 0, arr.Length);// Возвращает фактически считанное число байтов

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
                        var jsonFormatter = new DataContractJsonSerializer(typeof(Message));
                        Message mes = jsonFormatter.ReadObject(stream) as Message;// выполняем десериализацию
                        if (mes.Mes == "ExitOnline")
                        {
                            tcpClients.Remove(netstream);
                            netstream?.Close();
                            tcpClient?.Close();
                            return;
                        }
                        else if (mes.UserSenderId == 0 && mes.UserRecepientId == 0)
                        {
                            ListMessage listMessage = new ListMessage();
                            listMessage.listMessages= null;
                            listMessage.len = 0;
                            MemoryStream stream1 = new MemoryStream();
                            var jsonFormatter1 = new DataContractJsonSerializer(typeof(ListMessage));
                            jsonFormatter1.WriteObject(stream1, listMessage);
                            byte[] arr1 = stream1.ToArray(); // записываем содержимое потока в байтовый массив
                            stream1.Close();
                            netstream.Write(arr1, 0, arr1.Length);

                            tcpClients.Remove(netstream);
                            return;
                        }
                        if (mes.Mes == "CommandRemoveMessage")
                        {
                            RemoveMessage(mes);
                            HistoryMessage(netstream, mes);
                        }
                        else if (mes.Mes == "CommandRemoveAllMessages")
                        {
                            RemoveAllMessages(mes);
                            HistoryMessage(netstream, mes);
                        }
                        else if (mes.Mes == "Repeat")
                        {
                            HistoryMessageRepeat(netstream, mes);
                        }
                        else if (mes.Mes != "")
                        {
                            using (var db = new MessengerContext())
                            {
                                var Mes_edit = (from b in db.Messages
                                                where b.Id == mes.Id
                                                select b).SingleOrDefault();
                                if (Mes_edit != null)
                                {               
                                    Mes_edit.Mes = mes.Mes;
                                    db.SaveChanges();
                                    HistoryMessage(netstream, mes);
                                }
                                else
                                    NewMessage(netstream, mes);
                            }
                        }                        
                        else
                        {
                            HistoryMessage(netstream, mes);
                        }
                        stream.Close();
                    }
                }
                catch(IOException)
                {
                    tcpClients?.Remove(netstream);
                    netstream?.Close();
                    tcpClient?.Close();

                }
                catch (Exception ex)
                {
                    WriteLine("Server: " + ex.Message);
                    netstream?.Close();
                    tcpClient?.Close();
                }
                //finally
                //{
                //    netstream?.Close();
                //    tcpClient?.Close(); // закрываем TCP-подключение и освобождаем все ресурсы, связанные с объектом TcpClient.
                //}
            });
        }
        private async void HistoryMessage(NetworkStream netstream, Message mes, NetworkStream client = null)
        {
            await Task.Run(async () =>
            {
                try
                {
                    //проверяем есть ли такой пользователь в BD
                    using (var db = new MessengerContext())
                    {
                        var query = from b in db.Messages
                                    where b.UserSenderId == mes.UserSenderId && b.UserRecepientId == mes.UserRecepientId ||
                                    b.UserSenderId == mes.UserRecepientId && b.UserRecepientId == mes.UserSenderId
                                    select b;
                        List<Message> listMes = new List<Message>();
                        Message message = null;
                        foreach (var b in query)
                        {
                            message = new Message();
                            message.Id = b.Id;
                            message.UserSenderId = b.UserSenderId;
                            message.UserRecepientId = b.UserRecepientId;
                            message.Date_Time = b.Date_Time;
                            message.Mes = b.Mes;
                            message.MesAudio = b.MesAudio;
                            message.MesAudioUri = b.MesAudioUri;
                            listMes.Add(message);
                        }
                        ListMessage wrapper = new ListMessage();
                        wrapper.listMessages = listMes;
                        MemoryStream stream = new MemoryStream();
                        var jsonFormatter = new DataContractJsonSerializer(typeof(ListMessage));
                        jsonFormatter.WriteObject(stream, wrapper);
                        byte[] arr = stream.ToArray(); // записываем содержимое потока в байтовый массив
                        stream.Close();
                        
                        int length = arr.Length;
                        MesWrapper length1 = new MesWrapper();
                        length1.len= length;
                        length1.Mes = null;
                        MemoryStream stream1 = new MemoryStream();
                        var jsonFormatter1 = new DataContractJsonSerializer(typeof(MesWrapper));
                        jsonFormatter1.WriteObject(stream1, length1);
                        byte[] arr1 = stream1.ToArray(); // записываем содержимое потока в байтовый массив
                        stream1.Close();
                        netstream.Write(arr1, 0, arr1.Length);


                        if (client != null)
                        {
                            MesWrapper wrapper1 = new MesWrapper();
                            wrapper1.Mes = message;
                            wrapper1.len = -1;
                            MemoryStream stream2 = new MemoryStream();
                            var jsonFormatter2 = new DataContractJsonSerializer(typeof(MesWrapper));
                            jsonFormatter2.WriteObject(stream2, wrapper1);
                            byte[] arr2 = stream2.ToArray(); // записываем содержимое потока в байтовый массив
                            stream2.Close();
                            client.Write(arr2, 0, arr2.Length);
                        }
                    }
                }
                catch (Exception ex)
                {
                    WriteLine("Server: " + ex.Message);
                }
            });
        }
       
        private async void HistoryMessageRepeat(NetworkStream netstream, Message mes, NetworkStream client = null)
        {
                await Task.Run(async () =>
                {
                    try
                    {
                        //проверяем есть ли такой пользователь в BD
                        using (var db = new MessengerContext())
                        {
                            var query = from b in db.Messages
                                        where b.UserSenderId == mes.UserSenderId && b.UserRecepientId == mes.UserRecepientId ||
                                        b.UserSenderId == mes.UserRecepientId && b.UserRecepientId == mes.UserSenderId
                                        select b;
                            List<Message> listMes = new List<Message>();

                            foreach (var b in query)
                            {
                                Message message = new Message();
                                message.Id = b.Id;
                                message.UserSenderId = b.UserSenderId;
                                message.UserRecepientId = b.UserRecepientId;
                                message.Date_Time = b.Date_Time;
                                message.Mes = b.Mes;
                                message.MesAudio = b.MesAudio;
                                message.MesAudioUri = b.MesAudioUri;
                                listMes.Add(message);
                            }
                            ListMessage wrapper = new ListMessage();
                            wrapper.listMessages = listMes;
                            MemoryStream stream = new MemoryStream();
                            var jsonFormatter = new DataContractJsonSerializer(typeof(ListMessage));
                            jsonFormatter.WriteObject(stream, wrapper);
                            byte[] arr = stream.ToArray(); // записываем содержимое потока в байтовый массив
                            stream.Close();

                            netstream.Write(arr, 0, arr.Length);
                            
                        }
                    }
                    catch (Exception ex)
                    {
                        WriteLine("Server: " + ex.Message);
                    }
                });
        }
        private async void NewMessage(NetworkStream netstream, Message mes)
        {
            await Task.Run(async () =>
            {
                try
            {
                //проверяем есть ли такой пользователь в BD
                using (var db = new MessengerContext())
                {
                    User user = new User();
                    var query = from b in db.Users
                                where b.Id == mes.UserSenderId || b.Id == mes.UserRecepientId
                                select b;
                    foreach (var b in query)
                    {
                        if (b.Id == mes.UserRecepientId)
                            user = b;
                    }
                    if (query != null)
                    {
                        db.Messages.Add(mes);
                        db.SaveChanges();
                    }
                    NetworkStream UserRecepient = null;
                    foreach (var tsp in tcpClients)
                    {
                            string ip = tsp.Socket.RemoteEndPoint.ToString();
                            string IPRedact = ip.Substring(0, ip.IndexOf(":"));
                            if (user.IPadress == IPRedact)
                            {
                                UserRecepient = tsp;
                            }
                    }
                    HistoryMessage(netstream, mes, UserRecepient);
                }
            }
            catch (Exception ex)
            {
                WriteLine("Server: " + ex.Message);
            }
            });
        }
        private async void Exit(NetworkStream netstream, User user, TcpClient tcpClient)
        {
            await Task.Run(async () =>
            {
                using (var db = new MessengerContext())
                {
                    var us_exit = (from b in db.Users
                                   where b.IPadress == user.IPadress
                                   select b).Single();
                    if (us_exit.Password == user.Password) // проверка старого пароля в БД
                    {
                        // редактирование
                        us_exit.Online = user.Online;
                        db.SaveChanges();
                        string theReply = "User has logged out!";
                        WriteLine(user.Nick + " " + user.IPadress + " " + theReply);
                    }
                }
                clients.Remove(netstream);
                SendCollection();
            });
        }
        private async void ExitOnline(NetworkStream netstream, User user, TcpClient tcpClient)
        {
            await Task.Run(async () =>
            {
                using (var db = new MessengerContext())
                {
                    var us_exit = (from b in db.Users
                                     where b.IPadress == user.IPadress
                                     select b).Single();
                    if (us_exit.Password == user.Password) // проверка старого пароля в БД
                    {
                        // редактирование
                        us_exit.Online = user.Online;
                        db.SaveChanges();
                        string theReply = "User has logged out!";
                        WriteLine(user.Nick + " " + user.IPadress + " " + theReply);
                    }
                }
                clients.Remove(netstream);
                SendCollection();
            });
        }
        private void  RemoveMessage(Message mes)
        {
            try
            {
                //удаляем message из BD
                using (var db = new MessengerContext())
                {
                    var Mes_remove = (from b in db.Messages
                                     where b.Id == mes.Id
                                     select b).Single();

                    db.Remove(Mes_remove);
                    db.SaveChanges();                    
                }
            }
            catch (Exception ex)
            {
                WriteLine("Server: " + ex.Message);
            }
        }
        private void RemoveAllMessages(Message mes)
        {
            try
            {
                //удаляем all messages из BD
                using (var db = new MessengerContext())
                {
                    var Mes_remove = from b in db.Messages
                                      where b.UserSenderId == mes.UserSenderId && b.UserRecepientId == mes.UserRecepientId ||
                                            b.UserSenderId == mes.UserRecepientId && b.UserRecepientId == mes.UserSenderId
                                      select b;

                    db.RemoveRange(Mes_remove);
                    db.SaveChanges();
                }
            }
            catch (Exception ex)
            {
                WriteLine("Server: " + ex.Message);
            }
        }
    }
}
    