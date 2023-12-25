using static System.Console;
using MessengerModel;
using System.Runtime.Serialization.Json;
using System.Net.Sockets;
using System.Net;



namespace ServerAPP
{
    internal class Server
    {
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
                        //Receive(client);
                    }
                }
                catch (Exception ex)
                {
                    WriteLine("Сервер: " + ex.Message);
                }
            });
        }
    }
}
