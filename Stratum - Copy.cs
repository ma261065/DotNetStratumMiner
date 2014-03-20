using System;
using System.Collections;
using System.Diagnostics;
using System.Net.Sockets;
using System.Runtime.Serialization;
using System.Text;

namespace SharpMiner
{
    class Stratum
    {
        public event EventHandler<StratumEventArgs> GotSetDifficulty;
        public event EventHandler<StratumEventArgs> GotNotify;
        public event EventHandler<StratumEventArgs> GotResponse;

        private TcpClient tcpClient;
        private string page = "";
        public string ExtraNonce1 = "";
        public int ExtraNonce2 = 0;
        private string Server;
        private int Port;
        private string Username;
        private string Password;
        private int ID;
        private static Hashtable PendingACKs = new Hashtable();

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
            Byte[] bytesSent;

            // We are connected successfully
            try
            {
                NetworkStream networkStream = tcpClient.GetStream();

                // Send SUBSCRIBE request
                StratumCommand Command = new StratumCommand();
                Command.id = ID++;
                Command.method = "mining.subscribe";
                Command.parameters = new ArrayList();

                string request = Utilities.JsonSerialize(Command) + "\n";

                bytesSent = Encoding.ASCII.GetBytes(request);

                tcpClient.GetStream().Write(bytesSent, 0, bytesSent.Length);
                PendingACKs.Add(Command.id, Command.method);
                Console.WriteLine("Sent mining.subscribe");


                // Send AUTHORIZE request
                Command.id = ID++;
                Command.method = "mining.authorize";
                Command.parameters = new ArrayList();
                Command.parameters.Add(Username);
                Command.parameters.Add(Password);

                request = Utilities.JsonSerialize(Command) + "\n";

                bytesSent = Encoding.ASCII.GetBytes(request);

                tcpClient.GetStream().Write(bytesSent, 0, bytesSent.Length);
                PendingACKs.Add(Command.id, Command.method);
                Console.WriteLine("Sent mining.authorize");


                byte[] buffer = new byte[tcpClient.ReceiveBufferSize];

                // Now we are connected start async read operation.
                networkStream.BeginRead(buffer, 0, buffer.Length, ReadCallback, buffer);
            }
            catch (Exception ex)
            {
                Console.WriteLine("Socket error:" + ex.Message);
            }
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
                Console.WriteLine("Disconnected. Reconnecting...");
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
                else if (Response.result != null)       // We got a response
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

        public void Submit(string JobID, string nTime, string Nonce)
        {
            StratumCommand Command = new StratumCommand();
            Command.id = ID++;
            Command.method = "mining.submit";
            Command.parameters = new ArrayList();
            Command.parameters.Add(Username);
            Command.parameters.Add("1234");//JobID);
            Command.parameters.Add(ExtraNonce2.ToString("x8"));
            Command.parameters.Add(nTime);
            Command.parameters.Add(Nonce);

            string SubmitString = Utilities.JsonSerialize(Command) + "\n";

            Byte[] bytesSent = Encoding.ASCII.GetBytes(SubmitString);

            tcpClient.GetStream().Write(bytesSent, 0, bytesSent.Length);
            PendingACKs.Add(Command.id, Command.method);

            Console.WriteLine(DateTime.Now + " Submitting block " + JobID);
            Debug.WriteLine(DateTime.Now + " - Submit:" + SubmitString);
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
