// See https://aka.ms/new-console-template for more information

using CTraderManagerAPI;
using CTraderManagerAPI.Helpers;
using Google.Protobuf;
using System;
using System.Buffers;
using System.Net.Http;
using System.Net.Security;
using System.Net.Sockets;
using System.Reactive.Linq;
using System.Runtime.InteropServices;
using System.Security.Authentication;
using System.Text;
using System.Threading;
using System.Security.Cryptography;
using ProtoBuf;

namespace MyProject;
class Program
{
    //https://demo-rifx.webapi.ctrader.com:8443/webserv/
    const string proxyHost = "demo.p.ctrader.com";
    const int proxyPort = 5011;
    const long login = 30142;
    const string password = "n4wlyF";
    const string environment = "demo";
    const string plant = "chsandbox";

    static TcpClient _client;
    static SslStream _stream;
    static bool IsDisposed;
    static CancellationTokenSource _cancellationTokenSource=new();
    static DateTimeOffset LastSentMessageTime= DateTime.Now;
    static TimeSpan _heartbeatInterval = TimeSpan.FromSeconds(25);

    private static Client _cclient;
    private  const int MANUAL = 0;
    private const int AUTO = 1;
    private static readonly List<IDisposable> _disposables = new();
    private static readonly int mode = AUTO;
    static async Task Main(string[] args)
    {
        try
        {
            

            if (mode == AUTO) { 
                _cclient = new Client(proxyHost, proxyPort, _heartbeatInterval);
               // _disposables.Add(_cclient.Subscribe(OnMessageReceived, OnException));
                _disposables.Add(_cclient.Where(iMessage => iMessage is not ProtoHeartbeatEvent).Subscribe(OnMessageReceived, OnException));
               _disposables.Add(_cclient.Where(iMessage => iMessage is ProtoHeartbeatEvent).Subscribe(OnHeartbatReceived, OnException));

                Console.WriteLine("Connecting Client...");
                await _cclient.Connect();
                Console.WriteLine("Client successfully connected");
                Login();

                await Task.Delay(500);

            }
            else
            {            
                TcpClient client = new TcpClient();
                _stream = ConnectSSL(client, false);
                Login();
                ReadTcp(_cancellationTokenSource.Token);
                //ReadStream(_stream);
                await RunInBackground(_heartbeatInterval, async ()=> await  SendHeartbeat());
                //await SendHeartbeat();
                Console.WriteLine("Hello, World!");
            }
            
        }
        catch (Exception ex)
        {
            Console.WriteLine($"An exception occured: {ex.Message}");
        }
    }
    private static async void Login()
    {
        /*
            optional ProtoCSPayloadType payloadType = 1 [default = PROTO_MANAGER_AUTH_REQ];
            required string plantId = 2; // Identifier of the specific cServer instance
            required string environmentName = 3; // Identifier of the environment
            required int64 login = 4; // Login of the Manager
            required string passwordHash = 5; // MD5-hash of Manager's password
        */
        Console.WriteLine("Logging in ....");
        var hashedPass = CreateMD5(password);
        Console.WriteLine($"{hashedPass}"); 
        var applicationAuthReq = new ProtoManagerAuthReq()
        {
            PlantId = plant,
            EnvironmentName = environment,
            Login = login,
            PasswordHash = hashedPass,
        };
        var protoMessage = new ProtoMessage();
        Console.WriteLine(protoMessage.ToString()); 
        protoMessage.Payload = applicationAuthReq.ToByteString();
        protoMessage.PayloadType = (uint)ProtoCSPayloadType.ProtoManagerAuthReq;

        //await _cclient.SendMessage(applicationAuthReq);
        //await _cclient.SendMessageInstant(protoMessage);
        if(mode == MANUAL)await SendMessageInstant(protoMessage);
        else await _cclient.SendMessageInstant(protoMessage);
    }
    //https://stackoverflow.com/questions/11454004/calculate-a-md5-hash-from-a-string
    public static string CreateMD5(string input)
    {
        // Use input string to calculate MD5 hash
        using (System.Security.Cryptography.MD5 md5 = System.Security.Cryptography.MD5.Create())
        {
            byte[] inputBytes = System.Text.Encoding.ASCII.GetBytes(input);
            byte[] hashBytes = md5.ComputeHash(inputBytes);

            return Convert.ToHexString(hashBytes); // .NET 5 +

            // Convert the byte array to hexadecimal string prior to .NET 5
            // StringBuilder sb = new System.Text.StringBuilder();
            // for (int i = 0; i < hashBytes.Length; i++)
            // {
            //     sb.Append(hashBytes[i].ToString("X2"));
            // }
            // return sb.ToString();
        }
    }
    private static void OnMessageReceived(IMessage message)
    {
        Console.WriteLine($"\n{DateTime.Now}: Message Received:\n{message}");
        Console.WriteLine();
    }
    private static void OnHeartbatReceived(IMessage message)
    {
        Console.WriteLine($"\n{DateTime.Now}: Heartbeat Message Received:\n{message}");
        Console.WriteLine();
    }
    private static void OnException(Exception ex)
    {
        Console.WriteLine($"\n{DateTime.Now}: Exception\n: {ex}");
    }
    private static async Task RunInBackground(TimeSpan interval, Action action)
    {
        var periodicTimer = new PeriodicTimer(interval);
        while (await periodicTimer.WaitForNextTickAsync())
        {
            action();
        }
    }
    private static int GetLength(byte[] lengthBytes)
    {
        Span<byte> span = lengthBytes.AsSpan();
        span.Reverse();
        return BitConverter.ToInt32(span);
    }
    private static SslStream ConnectSSL(TcpClient client, bool remainOpen)
    {
        client.Connect(proxyHost, proxyPort);
        SslStream stream = new SslStream(client.GetStream(), remainOpen);

        try
        {
            stream.AuthenticateAsClient(proxyHost);
            return stream;
        }
        catch (AuthenticationException e)
        {
            Console.WriteLine("Exception: {0}", e.Message);
            if (e.InnerException != null)
            {
                Console.WriteLine("Inner exception: {0}", e.InnerException.Message);
            }
            Console.WriteLine("Authentication failed - closing the connection.");
            client.Close();
            return stream;
        }

    }

    private static void ReadStream(SslStream stream)
    {
        byte[] dataLength1 = new byte[4];
        byte[] data1 = null;
        bool isDone = false;

        try
        {
            int num = 0;
            while (!isDone)
            {
                do
                {
                    int count = (int)dataLength1.Length - num;
                    if (dataLength1.Length > 0 && count < 0)
                    {
                        isDone = true;
                        break;
                    }
                    int num2 = num;
                    num = num2 + stream.Read(dataLength1, num, count);//.ConfigureAwait(continueOnCapturedContext: false);
                    if (num == 0)
                    {
                        throw new InvalidOperationException("Remote host closed the connection");
                    }
                }
                while (num < dataLength1.Length);
                int length1 = GetLength(dataLength1);
                if (length1 <= 0)
                {
                    continue;
                }
                data1 = ArrayPool<byte>.Shared.Rent(length1);
                num = 0;
                do
                {
                    int count2 = length1 - num;
                    int num2 = num;
                    num = num2 + stream.Read(data1, num, count2);//.ConfigureAwait(continueOnCapturedContext: false);
                    if (num == 0)
                    {
                        throw new InvalidOperationException("Remote host closed the connection");
                    }
                }
                while (num < length1);
                var protoMessage = ProtoMessage.Parser.ParseFrom(data1, 0, length1);
                //Console.WriteLine(protoMessage.ToString());
                Console.WriteLine("Payload Type: " + ((ProtoCSPayloadType)protoMessage.PayloadType == ProtoCSPayloadType.ProtoHelloEvent ? "Hello Event" : protoMessage.PayloadType));
                //var protoMessage = Serializer.Deserialize<Hello>(new MemoryStream(data1));
                ArrayPool<byte>.Shared.Return(data1);
            }

        }
        catch (Exception e) { }


    }

    /// <summary>
    /// This method will read the TCP stream for incoming messages
    /// </summary>
    /// <param name="cancellationToken">The cancellation token that will be used on ReadAsync calls</param>
    /// <returns>Task</returns>
    private static async void ReadTcp(CancellationToken cancellationToken)
    {
        var dataLength = new byte[4];
        byte[] data = null;
        try
        {
            while (!IsDisposed)
            {
                var readBytes = 0;
                do
                {
                    var count = dataLength.Length - readBytes;
                    readBytes += await _stream.ReadAsync(dataLength, readBytes, count, cancellationToken).ConfigureAwait(false);

                    if (readBytes == 0) throw new InvalidOperationException("Remote host closed the connection");
                }
                while (readBytes < dataLength.Length);

                var length = GetLength(dataLength);

                if (length <= 0) continue;

                data = ArrayPool<byte>.Shared.Rent(length);

                readBytes = 0;

                do
                {
                    var count = length - readBytes;
                    readBytes += await _stream.ReadAsync(data, readBytes, count, cancellationToken).ConfigureAwait(false);

                    if (readBytes == 0) throw new InvalidOperationException("Remote host closed the connection");
                }
                while (readBytes < length);
                var message = ProtoMessage.Parser.ParseFrom(data, 0, length);          
                ArrayPool<byte>.Shared.Return(data);

                //TODO messagefactory
                OnNext(message);
            }
        }
        catch (Exception ex)
        {
            if (data is not null) ArrayPool<byte>.Shared.Return(data);

            var exception = new Exception(ex.ToString());

            //OnError(exception);
        }
    }
    /// <summary>
    /// Calls each observer OnNext with the message
    /// </summary>
    /// <param name="protoMessage">Message</param>
    private static void OnNext(ProtoMessage protoMessage)
    {
        try
        {
            var message = MessageFactory.GetMessage(protoMessage);
            Console.WriteLine(DateTime.Now+" : Message received: "+ protoMessage.ToString());            

        }
        catch (Exception ex)
        {
            Console.WriteLine(ex.ToString());
        }
        
    }
    private static async Task SendHeartbeat()
    {
        if (IsDisposed || DateTimeOffset.Now - LastSentMessageTime < _heartbeatInterval) return;
        Console.WriteLine(DateTime.Now + " : Sending heartbeat");
         try
         {
            ProtoHeartbeatEvent heartbeatEvent = new ProtoHeartbeatEvent();
            var payloadType = (uint)ProtoPayloadType.HeartbeatEvent;
            var payload = heartbeatEvent.ToByteString();
            var protoMessage = GetMessage(payloadType, payload);
            await SendMessageInstant(protoMessage);
         }
         catch (Exception ex)
         {
            Console.WriteLine(ex.ToString());
             //OnError(ex);
         }
    }
    //message.ToByteString()
    private static async Task SendLoginRequest()
    {
        if (IsDisposed ) return;
        Console.WriteLine(DateTime.Now + " : Sending Login Request");
        try
        {
            ProtoManagerAuthReq heartbeatEvent = new ProtoManagerAuthReq();
            var payloadType = (uint)ProtoPayloadType.RegisterCserverConnectionReq;
            var payload = heartbeatEvent.ToByteString();
            var protoMessage = GetMessage(payloadType, payload);
            await SendMessageInstant(protoMessage);
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex.ToString());
            //OnError(ex);
        }


    }
    private static ProtoMessage GetMessage(uint payloadType, ByteString payload, string clientMessageId = null)
    {
        var message = new ProtoMessage()
        {
            PayloadType = payloadType,
            Payload = payload,
        };
        if (!string.IsNullOrEmpty(clientMessageId))
        {
            message.ClientMsgId = clientMessageId;
        }

        return message;

    }
    /// <summary>
    /// Writes the message bytes to TCP stream
    /// </summary>
    /// <param name="messageByte"></param>
    /// <param name="cancellationToken">The cancellation token that will be used on calling stream methods</param>
    /// <returns>Task</returns>
    private static async Task WriteTcp(byte[] messageByte, CancellationToken cancellationToken)
    {
        var data = BitConverter.GetBytes(messageByte.Length).Reverse().Concat(messageByte).ToArray();

        await _stream.WriteAsync(data, 0, data.Length, cancellationToken).ConfigureAwait(false);

        await _stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }
    public static async Task SendMessageInstant(ProtoMessage message)
    {
        ThrowObjectDisposedExceptionIfDisposed();

        try
        {
            var messageByte = message.ToByteArray();

           await WriteTcp(messageByte, _cancellationTokenSource.Token);
           

            LastSentMessageTime = DateTimeOffset.Now;
        }
        catch (Exception ex)
        {
            // var exception = new SendException(ex);
            IsDisposed = true;
            throw ex;
        }
    }
    private static void ThrowObjectDisposedExceptionIfDisposed()
    {
        Console.WriteLine("Disposed");
        //if (IsDisposed) throw new ObjectDisposedException(GetType().FullName);
    }
    /// <summary>
    /// This method will keep reading the messages channel and then it will send the read message
    /// </summary>
    /// <param name="cancellationToken">The cancellation token that will cancel the reading</param>
    /// <returns>Task</returns>
   /* private async Task StartSendingMessages(CancellationToken cancellationToken)
    {
        try
        {
            while (await _messagesChannel.Reader.WaitToReadAsync(cancellationToken) && IsDisposed is false && IsTerminated is false)
            {
                while (_messagesChannel.Reader.TryRead(out var message))
                {
                    var timeElapsedSinceLastMessageSent = DateTimeOffset.Now - LastSentMessageTime;

                    if (timeElapsedSinceLastMessageSent < _requestDelay)
                    {
                        await Task.Delay(_requestDelay - timeElapsedSinceLastMessageSent);
                    }

                    if (IsDisposed is false)
                    {
                        await SendMessageInstant(message);
                    }

                    if (MessagesQueueCount > 0) MessagesQueueCount -= 1;
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            //OnError(ex);
        }
    }*/

}