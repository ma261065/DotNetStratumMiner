using System;
using System.Collections;
using System.Diagnostics;
using System.Net.Sockets;
using System.Runtime.Serialization;
using System.Text;
using System.Diagnostics;

namespace DotNetStratumMiner
{
    class Stratum
    {
        public event EventHandler<StratumEventArgs> GotSetDifficulty;
        public event EventHandler<StratumEventArgs> GotNotify;
        public event EventHandler<StratumEventArgs> GotResponse;

        public static Hashtable PendingACKs = new Hashtable();
        public TcpClient tcpClient;
        private int SharesSubmitted = 0;
        private string page = "";
        public string ExtraNonce1 = "";
        public int ExtraNonce2 = 0;
        private string Server;
        private int Port;
        private string Username;
        private string Password;
        public int ID;

        public void ConnectToServer(string MineServer, int MinePort, string MineUser, string MinePassword)
        {
            try
            {
                ID = 1;
                Server = MineServer;
                Port = MinePort;
                Username = MineUser;
                Password = MinePassword;
                tcpClient = new TcpClient(AddressFamily.InterNetwork);

                // Start an asynchronous connection
                tcpClient.BeginConnect(Server, Port, new AsyncCallback(ConnectCallback), tcpClient);
            }
            catch (Exception ex)
            {
                Console.WriteLine("Socket error:" + ex.Message);
            }
        }

        private void ConnectCallback(IAsyncResult result)
        {
            // We are connected successfully
            try
            {
                SendSUBSCRIBE();
                SendAUTHORIZE();

                NetworkStream networkStream = tcpClient.GetStream();
                byte[] buffer = new byte[tcpClient.ReceiveBufferSize];

                // Now we are connected start async read operation.
                networkStream.BeginRead(buffer, 0, buffer.Length, ReadCallback, buffer);
            }
            catch (Exception ex)
            {
                Console.WriteLine("Socket error:" + ex.Message);
            }
        }

        public void SendSUBSCRIBE()
        {
            Byte[] bytesSent;
            StratumCommand Command = new StratumCommand();
            
            Command.id = ID++;
            Command.method = "mining.subscribe";
            Command.parameters = new ArrayList();

            string request = Utilities.JsonSerialize(Command) + "\n";

            bytesSent = Encoding.ASCII.GetBytes(request);

            try
            {
                tcpClient.GetStream().Write(bytesSent, 0, bytesSent.Length);
                PendingACKs.Add(Command.id, Command.method);
            }
            catch (Exception ex)
            {
                Console.WriteLine("Socket error:" + ex.Message);
                ConnectToServer(Server, Port, Username, Password);
            }
            
            Console.WriteLine("Sent mining.subscribe");
        }

        public void SendAUTHORIZE()
        {
            Byte[] bytesSent;
            StratumCommand Command = new StratumCommand();

            Command.id = ID++;
            Command.method = "mining.authorize";
            Command.parameters = new ArrayList();
            Command.parameters.Add(Username);
            Command.parameters.Add(Password);

            string request = Utilities.JsonSerialize(Command) + "\n";

            bytesSent = Encoding.ASCII.GetBytes(request);

            try
            {
                tcpClient.GetStream().Write(bytesSent, 0, bytesSent.Length);
                PendingACKs.Add(Command.id, Command.method);
            }
            catch(Exception ex)
            {
                Console.WriteLine("Socket error:" + ex.Message);
                ConnectToServer(Server, Port, Username, Password);
            }
            
            Console.WriteLine("Sent mining.authorize");
        }

        // Callback for Read operation
        private void ReadCallback(IAsyncResult result)
        {
            NetworkStream networkStream;
            int bytesread;
            
            byte[] buffer = result.AsyncState as byte[];
            
            try
            {
                networkStream = tcpClient.GetStream();
                bytesread = networkStream.EndRead(result);
            }
            catch (Exception ex)
            {
                Console.WriteLine("Socket error:" + ex.Message);
                return;
            }

            if (bytesread == 0)
            {
                Console.WriteLine(DateTime.Now +  " Disconnected. Reconnecting...");
                Debug.WriteLine(DateTime.Now + " Disconnected. Reconnecting...");
                tcpClient.Close();
                tcpClient = null;
                PendingACKs.Clear();
                ConnectToServer(Server, Port, Username, Password);
                return;
            }

            // Get the data
            string data = ASCIIEncoding.ASCII.GetString(buffer, 0, bytesread);
            Debug.WriteLine(data);

            page = page + data;

            int FoundClose = page.IndexOf('}');

            while (FoundClose > 0)
            {
                string CurrentString = page.Substring(0, FoundClose + 1);

                // We can get either a command or response from the server. Try to deserialise both
                StratumCommand Command = Utilities.JsonDeserialize<StratumCommand>(CurrentString);
                StratumResponse Response = Utilities.JsonDeserialize<StratumResponse>(CurrentString);

                StratumEventArgs e = new StratumEventArgs();

                if (Command.method != null)             // We got a command
                {
                    Debug.WriteLine(DateTime.Now + " Got Command: " + CurrentString);
                    e.MiningEventArg = Command;

                    switch (Command.method)
                    {
                        case "mining.notify":
                            if (GotNotify != null)
                                GotNotify(this, e);
                            break;
                        case "mining.set_difficulty":
                            if (GotSetDifficulty != null)
                                GotSetDifficulty(this, e);
                            break;
                    }
                }
                else if (Response.error != null || Response.result != null)       // We got a response
                {
                    Debug.WriteLine(DateTime.Now + " Got Response: " + CurrentString);
                    e.MiningEventArg = Response;

                    // Find the command that this is the response to and remove it from the list of commands that we're waiting on a response to
                    string Cmd = (string)PendingACKs[Response.id];
                    PendingACKs.Remove(Response.id);

                    if (Cmd == null)
                        Console.WriteLine("Unexpected Response");
                    else if (GotResponse != null)
                        GotResponse(Cmd, e);
                }

                page = page.Remove(0, FoundClose + 2);
                FoundClose = page.IndexOf('}');
            }

            // Then start reading from the network again.
            networkStream.BeginRead(buffer, 0, buffer.Length, ReadCallback, buffer);
        }

        public void Submit(string JobID, string nTime, string Nonce, int Difficulty)
        {
            StratumCommand Command = new StratumCommand();
            Command.id = ID++;
            Command.method = "mining.submit";
            Command.parameters = new ArrayList();
            Command.parameters.Add(Username);
            Command.parameters.Add(JobID);
            Command.parameters.Add(ExtraNonce2.ToString("x8"));
            Command.parameters.Add(nTime);
            Command.parameters.Add(Nonce);

            string SubmitString = Utilities.JsonSerialize(Command) + "\n";

            Byte[] bytesSent = Encoding.ASCII.GetBytes(SubmitString);

            try
            {
                tcpClient.GetStream().Write(bytesSent, 0, bytesSent.Length);
                PendingACKs.Add(Command.id, Command.method);
            }
            catch (Exception ex)
            {
                Console.WriteLine("Socket error:" + ex.Message);
                ConnectToServer(Server, Port, Username, Password);
            }

            SharesSubmitted++;
            Console.WriteLine("Submit (Difficulty {0})", Difficulty);
            Debug.WriteLine("[{0}] Submit (Difficulty {1}) : {2}", DateTime.Now, Difficulty, SubmitString);
        }
    }

    [DataContract]
    public class StratumCommand
    {
        [DataMember]
        public string method;
        [DataMember]
        public System.Nullable<int> id;
        [DataMember(Name = "params")]
        public ArrayList parameters;
    }

    [DataContract]
    public class StratumResponse
    {
        [DataMember]
        public ArrayList error;
        [DataMember]
        public System.Nullable<int> id;
        [DataMember]
        public object result;
    }

    public class StratumEventArgs:EventArgs
    {
        public object MiningEventArg;
    }
}
