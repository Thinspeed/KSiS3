using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Linq;

namespace KSiS2
{
	class MessagePacket
    {
		public byte Type;
		public string Content;

		public MessagePacket(byte type, string content)
        {
			Type = type;
			Content = content;
        }

		public MessagePacket(byte[] data)
        {
			Type = data[0];
			Content = Encoding.UTF8.GetString(data, 1, data.Length - 1);
        }

		public byte[] GetBytes()
        {
			var paylod = Encoding.UTF8.GetBytes(Content);
			var data = new byte[paylod.Length + 1];
			data[0] = Type;
			paylod.CopyTo(data, 1);
			return data;
        }
    }

	class User
	{
		public string Name { get; set; }
		public IPAddress Ip { get; set; }

		IPAddress remoteAddress = IPAddress.Parse("255.255.255.255");
		int remotePort = 8754;
		UdpClient udpClient;

		public Server Server;

		public User(string name)
		{
			Name = name;
			var host = Dns.GetHostEntry(Dns.GetHostName());
			Ip = host.AddressList[0];
			Server = new Server(this);
			SendName();
			Thread sendMessageTread = new Thread(new ThreadStart(SendMessage));
			sendMessageTread.Start();
		}

		void SendMessage()
        {
			try
            {
				while (true)
				{
					string message = Console.ReadLine();
					byte[] data = (new MessagePacket(1, message)).GetBytes();
					Server.SendAllUsers(data);
				}
			}
            catch (Exception e)
            {
				Console.WriteLine(e.Message);
            }
		}

		void SendName()
		{
			udpClient = new UdpClient();
			IPEndPoint endPoint = new IPEndPoint(remoteAddress, remotePort);
			udpClient.EnableBroadcast = true;
			var udpMassege = Encoding.UTF8.GetBytes(Name);
			udpClient.Send(udpMassege, udpMassege.Length, endPoint);
			udpClient.Close();
		}
	}
	
	class ConnectedUser
    {
		User user;
		public IPAddress Ip { get; private set; }
		public string Name { get; private set; }

		TcpClient tcpClient;
		NetworkStream stream = null;
		int tcpPort = 8755;

		public ConnectedUser(IPAddress ip, string name, User user)
        {
			Ip = ip;
			Name = name;
			this.user = user;
			var iPEndPoint = new IPEndPoint(Ip, tcpPort);
			tcpClient = new TcpClient();
			tcpClient.Connect(iPEndPoint);
			Thread userThread = new Thread(new ThreadStart(Listen));
			userThread.Start();
        }

		public ConnectedUser(TcpClient tcpClient, User user)
        {
			this.tcpClient = tcpClient;
			this.user = user;
			Ip = IPAddress.Parse(tcpClient.Client.RemoteEndPoint.ToString().Split(':')[0]);
			Name = "undef";
			Thread userThread = new Thread(new ThreadStart(Listen));
			userThread.Start();
		}

		void Listen()
        {
            try
            {
				stream = tcpClient.GetStream();
				Console.WriteLine("Поток запущен");
				var name = (new MessagePacket(0, user.Name)).GetBytes();
				stream.Write(name, 0, name.Length);
				while (true)
				{
					var packet = new MessagePacket(GetMessage());
					if (packet.Type == 0)
                    {
						Name = packet.Content;
                    }
					else
					{
						Console.WriteLine("{0} ({1}) : {2}", Name, Ip, packet.Content);
					}
				}
			}
			catch (Exception e)
            {
				Console.WriteLine(e.Message);
            }
            finally
            {
				if (stream != null)
                {
					stream.Close();
                }

				tcpClient.Close();
				user.Server.Disconnect(this);
            }
        }

		byte[] GetMessage()
        {
			byte[] data = new byte[64];
			var packet = new List<byte>();
			StringBuilder builder = new StringBuilder();
			int bytes = 0;
			do
			{
				bytes = stream.Read(data, 0, data.Length);
				packet.AddRange(data.Take(bytes));
			}
			while (stream.DataAvailable);

			return packet.ToArray();
		}

		public void WriteToStream(byte[] data)
        {
			if (stream != null)
            {
				stream.Write(data, 0, data.Length);
			}
        }
    }

	class Server
    {
		User myUser;
		List<ConnectedUser> users = new List<ConnectedUser>();
		UdpClient nameReceiver = new UdpClient();
		int udpPort = 8754;

		Stack<TcpClient> tcpClients = new Stack<TcpClient>();
		int tcpPort = 8755;

		public Server(User user)
        {
			this.myUser = user;
			Thread listenThread = new Thread(new ThreadStart(Listen));
			listenThread.Start();
			Thread nameReciveThread = new Thread(new ThreadStart(ReceiveNewUsers));
			nameReciveThread.Start();
		}

		void ReceiveNewUsers()
		{
			nameReceiver = new UdpClient(udpPort);
			nameReceiver.EnableBroadcast = true;
			IPEndPoint remoteIp = null;
			try
			{
				while (true)
				{
					byte[] data = nameReceiver.Receive(ref remoteIp);
					string name = Encoding.UTF8.GetString(data);
					if (!IsIpAdressMy(remoteIp.Address))
                    {
						users.Add(new ConnectedUser(remoteIp.Address, name, myUser));
						Console.ForegroundColor = ConsoleColor.Green;
						Console.WriteLine("{0} подключился", name);
						Console.ResetColor();
					}
				}
			}
			catch (Exception ex)
			{
				Console.WriteLine(ex.Message);
			}
			finally
			{
				nameReceiver.Close();
			}
		}

		bool IsIpAdressMy(IPAddress iPAddress)
        {
			foreach (var myIPAdress in Dns.GetHostEntry(Dns.GetHostName()).AddressList)
            {
				if (iPAddress.Equals(myIPAdress))
                {
					return true;
                }
            }

			return false;
        }

		void Listen()
        {
			TcpListener listener = null;
			try
            {
				listener = new TcpListener(IPAddress.Any, tcpPort);
				listener.Start();
				while (true)
                {
					var tcpClient = listener.AcceptTcpClient();
					users.Add(new ConnectedUser(tcpClient, myUser));
                }
            }
			catch (Exception e)
            {
				Console.WriteLine(e.Message);
            }
			finally
            {
				if (listener != null)
                {
					listener.Stop();
                }
            }
        }

		public void Disconnect(ConnectedUser connectedUser)
        {
			if (users.Contains(connectedUser))
            {
				Console.ForegroundColor = ConsoleColor.Red;
				Console.WriteLine("{0} покинул чат", connectedUser.Name);
				Console.ResetColor();
				users.Remove(connectedUser);
            }
        }

		public void SendAllUsers(byte[] data)
        {
			foreach (var user in users)
            {
				user.WriteToStream(data);
            }
        }
	}

	class Program
	{
		static void Main(string[] args)
		{
			var user = new User(Console.ReadLine());
		}
	}
}
