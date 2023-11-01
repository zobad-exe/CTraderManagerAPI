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
using RabbitMQ.Client;
using Newtonsoft.Json;
using CTraderManagerAPI.Model;

namespace MyProject;
class Program
{
    //https://demo-rifx.webapi.ctrader.com:8443/webserv/
    //const string proxyHost = "demo.p.ctrader.com";

    private static IModel channel;
    private static IConnection conn;
    private static string mqURI = "amqp://guest:guest@localhost:5672";
    //private static string mqURI = "amqp://admin:admin123@40.112.91.77:5672";

    //ztfx live
    /*const string proxyHost = "live.p.ctrader.com";
    const int proxyPort = 5011;
    const long login = 10012;
    const string password = "wKNbKP";*/
    //ztfx demo
    /*const string proxyHost = "demo.p.ctrader.com";
    const int proxyPort = 5011;
    const long login = 30004;
    const string password = "gRmGc6";
    const string md5Hash = "658971d8379f7c170dc7e96c825da968";
    const string environment = "live";
    const string plant = "rifx";*/
    //sandbox
    const string proxyHost = "uat-demo.p.ctrader.com";
    const int proxyPort = 5011;
    const long login = 30142;
    const string password = "n4wlyF";
    const string md5Hash = "658971d8379f7c170dc7e96c825da968";
    const string environment = "demo";
    const string plant = "chsandbox";

    static TcpClient _client;
    static SslStream _stream;
    static bool IsDisposed;
    static CancellationTokenSource _cancellationTokenSource=new();
    static DateTimeOffset LastSentMessageTime= DateTime.Now;
    static TimeSpan _heartbeatInterval = TimeSpan.FromSeconds(25);
    static Dictionary<long, AccountsLite>  accountsDictionary = new Dictionary<long, AccountsLite>();   
    static Dictionary<long, string>  symbolsDictionary = new Dictionary<long, string>();   

    private static Client _cclient;
    private  const int MANUAL = 0;
    private const int AUTO = 1;
    private static readonly List<IDisposable> _disposables = new();
    private static readonly int mode = AUTO;
    static async Task Main(string[] args)
    {
        try
        {
            //MQ initialize
            var factory = new ConnectionFactory
            {
                Uri = new Uri(mqURI)
            };
            conn = factory.CreateConnection();
            channel = conn.CreateModel();
            channel.ExchangeDeclare("ctrader-sandbox-exchange", ExchangeType.Direct);

            if (mode == AUTO) { 
                _cclient = new Client(proxyHost, proxyPort, _heartbeatInterval);
               // _disposables.Add(_cclient.Subscribe(OnMessageReceived, OnException));
                _disposables.Add(_cclient.Where(iMessage => iMessage is not ProtoHeartbeatEvent).Subscribe(OnMessageReceived, OnException));
                _disposables.Add(_cclient.Where(iMessage => iMessage is ProtoHeartbeatEvent).Subscribe(OnHeartbatReceived, OnException));
                _disposables.Add(_cclient.Where(iMessage => iMessage is ProtoTraderListRes).Subscribe(OnTradersReceived, OnException));

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
                
            }
                Console.WriteLine("Hello, World!");
                Console.ReadKey();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"An exception occured: {ex.Message}");
        }
    }
    private static async void Login()
    {       
        Console.WriteLine("Logging in ....");
        var hashedPass = CreateMD5(password).ToLower();
        Console.WriteLine($"Autogen hash: {hashedPass}"); 
        Console.WriteLine($"Manual hash: {md5Hash}"); 
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
        try { 
            var rawMsg = (ProtoMessage)message;
            var payload =rawMsg.Payload;
            var type = message.GetPayloadType();
            if(type == (uint)ProtoCSPayloadType.ProtoHelloEvent)
            {
                OnHelloReceived(message);
            }
            if (type == (uint)ProtoCSPayloadType.ProtoTraderListRes)
                OnTradersReceived(message);
            if (type == (uint)ProtoCSPayloadType.ProtoManagerAuthRes)
                OnLoginReceived(message);
            if (type == (uint)ProtoCSPayloadType.ProtoManagerSymbolListRes)
            {
                OnSymbolsReceived(message);
            }
            if (type == (uint)ProtoCSPayloadType.ProtoExecutionEvent) {
                OnExecutionMessageReceived(message);
                /*Console.WriteLine("Trade Execution Event...");
                var _msg = ProtoExecutionEvent.Parser.ParseFrom(rawMsg.Payload);
                Console.WriteLine("Reply : " + _msg);
                if(_msg.ExecutionType == ProtoExecutionType.OrderFilled)
                {

                }

                var dto = new DealsDTO();
                dto.platform = "CTRADER";
                string dealId = _msg?.Deal?.DealId.ToString() ?? _msg?.Position?.PositionId.ToString() ??"0";
                dto.dealId = int.Parse(dealId);
                dto.positionId = (int)_msg?.Position?.PositionId;
                dto.login = (int)_msg?.Order?.Login;
                dto.traderId = (int)_msg?.Position?.TradeData?.TraderId ;
                if (_msg.Position?.TradeData?.TradeSide == ProtoTradeSide.Buy)
                    dto.action = "0";
                else if (_msg.Position?.TradeData?.TradeSide == ProtoTradeSide.Sell)
                    dto.action = "1";
                if(_msg.Position?.PositionStatus == ProtoPositionStatus.PositionStatusOpen)
                    dto.entry = "0";
                else if( _msg.Position?.PositionStatus == ProtoPositionStatus.PositionStatusClosed)
                    dto.entry = "1";             
                dto.time = _msg.Position?.UtcLastUpdateTimestamp.ToString() ?? "";
                dto.symbol = _msg.Position?.TradeData?.SymbolId.ToString() ?? "";
                dto.volume = (int)_msg.Position?.TradeData?.Volume;
                dto.rateProfit = "0";
                dto.rateMargin = _msg.Position?.MarginRate.ToString() ?? "";
                dto.comment =_msg.Position?.TradeData?.Comment ?? "";
                dto.profit = 0;
                dto.volumeClosed = 0;
                dto.contractSize = 0;
                dto.price = (double)_msg.Position?.Price;
                dto.priceSL = _msg.Position.StopLoss;
                dto.priceTP = _msg.Position.TakeProfit;

                bool isOpen = (_msg.Position.PositionStatus == ProtoPositionStatus.PositionStatusOpen
                            && _msg.Position.PositionStatus != ProtoPositionStatus.PositionStatusError
                    );

                PublishRabbitMq(dto,isOpen );*/
            }
        } catch (Exception ex) { OnException(ex); }
        //Console.WriteLine($"\n{DateTime.Now}: Message Received:\n{message}");
        //Console.WriteLine();
    }
    private static void OnHelloReceived(IMessage message)
    {
        try { 
            var rawMsg = (ProtoMessage)message;
            if (message.GetPayloadType() == (uint)ProtoCSPayloadType.ProtoHelloEvent)
            {
                var msg = ProtoHelloEvent.Parser.ParseFrom(rawMsg.Payload);
                Console.WriteLine(msg);
            }      
        
        } catch (Exception ex) { OnException(ex); }
    }
    private static void OnSymbolsReceived(IMessage message)
    {
        try { 
            var rawMsg = (ProtoMessage)message;
            if(message.GetPayloadType() == (uint)ProtoCSPayloadType.ProtoManagerSymbolListRes)
            {
                var msg = ProtoManagerSymbolListRes.Parser.ParseFrom(rawMsg.Payload);
                Console.WriteLine($"\n{DateTime.Now}: Traders list Message Received:\n");
                foreach(var symbol in msg.Symbol)
                {
                    Console.WriteLine($"symbol: {symbol.Name} ID: {symbol.SymbolId}");
                    symbolsDictionary.TryAdd(key: symbol.SymbolId, value:symbol.Name );
                }
                Console.WriteLine();
            }        
        
        } catch (Exception ex) { OnException(ex); }
        
    }
    private static void OnTradersReceived(IMessage message)
    {
        try {
            var rawMsg = (ProtoMessage)message;
            if(message.GetPayloadType() == (uint)ProtoCSPayloadType.ProtoTraderListRes)
            {
                var msg = ProtoTraderListRes.Parser.ParseFrom(rawMsg.Payload);
                Console.WriteLine($"\n{DateTime.Now}: Traders list Message Received:\n");
                foreach(var trader in msg.Trader)
                {
                    Console.WriteLine($"email: {trader.Email} trader ID: {trader.TraderId}  login: {trader.Login} balance: {trader.Balance}");
                    accountsDictionary.TryAdd(key: trader.TraderId, value:new AccountsLite(traderId: trader.TraderId, login: trader.Login, email: trader.Email, trader.Name, trader.LastName) );
                }
                Console.WriteLine();
            }        
        } catch (Exception ex) { OnException(ex); }
    }
    private static void OnHeartbatReceived(IMessage message)
    {
        Console.WriteLine($"\n{DateTime.Now}: Heartbeat Message Received:\n{message}");
        Console.WriteLine();
    }
    private static void OnLoginReceived(IMessage message)
    {
        try { 
            Console.WriteLine("Login response...");
            var rawMsg = (ProtoMessage)message;
            var _msg = ProtoManagerAuthRes.Parser.ParseFrom(rawMsg.Payload);
            Console.WriteLine("Message : " + _msg);
        
        
            _cclient.RequestTraderEntities();  
            _cclient.RequestSymbols();
        
        } catch (Exception ex) { OnException(ex); }
    }
    private static void OnExecutionMessageReceived(IMessage message)
    {
        //_cclient.RequestTraderEntities();
        try { 
            var rawMsg = (ProtoMessage)message;
            if (message.GetPayloadType() == (uint)ProtoCSPayloadType.ProtoExecutionEvent)
            {
                Console.WriteLine("Trade Execution Event...");
                var _msg = ProtoExecutionEvent.Parser.ParseFrom(rawMsg.Payload);
                Console.WriteLine("Reply : " + _msg);
                if (_msg.ExecutionType == ProtoExecutionType.OrderFilled)
                {
                    var dto = new DealsDTO();
                    AccountsLite account;
                    string _symbol;
                    bool isLogin = accountsDictionary.TryGetValue(_msg.Position.TradeData.TraderId, out account);
                    bool isSymbol = symbolsDictionary.TryGetValue(_msg.Position.TradeData.SymbolId, out _symbol);
                    dto.platform = "CTRADER";
                    string dealId = _msg?.Deal?.DealId.ToString() ?? _msg?.Position?.PositionId.ToString() ?? "0";
                    dto.dealId = int.Parse(dealId);
                    dto.positionId = (int)_msg?.Position?.PositionId;
                    dto.login = isLogin
                                       ? (int)account.Login
                                       : (int)_msg?.Order.Login;

                    dto.traderId = (int)_msg?.Position?.TradeData?.TraderId;
                    if (_msg.Position?.TradeData?.TradeSide == ProtoTradeSide.Buy)
                        dto.action = "0";
                    else if (_msg.Position?.TradeData?.TradeSide == ProtoTradeSide.Sell)
                        dto.action = "1";
                    if (_msg.Position?.PositionStatus == ProtoPositionStatus.PositionStatusOpen)
                        dto.entry = "0";
                    else if (_msg.Position?.PositionStatus == ProtoPositionStatus.PositionStatusClosed)
                        dto.entry = "1";
                    dto.time = _msg.Position?.UtcLastUpdateTimestamp.ToString() ?? "";
                    dto.symbol = isSymbol ?
                                    _symbol
                                    : _msg.Position?.TradeData?.SymbolId.ToString() ?? "";
                    dto.volume = (int)_msg?.Position?.TradeData?.Volume > 0
                                ? (int)_msg?.Position?.TradeData?.Volume
                                : (int)_msg?.Order?.TradeData?.Volume > 0
                                ? (int)_msg?.Order?.TradeData?.Volume
                                : (int)_msg?.Order?.ExecutedVolume > 0
                                ? (int)_msg?.Order?.ExecutedVolume
                                : (int)_msg?.Deal?.FilledVolume > 0
                                ? (int)_msg?.Deal?.FilledVolume : 0
                                ;
                    //if (dto.volume > 0) dto.volume /= 100;
                    dto.rateProfit = "0";
                    dto.rateMargin = _msg.Position?.MarginRate.ToString() ?? "";
                    dto.comment = _msg.Position?.TradeData?.Comment ?? "";
                    dto.profit = _msg?.Deal?.ClosePositionDetail?.NetProfit ?? 0;
                    dto.volumeClosed = _msg.Deal?.ClosePositionDetail?.ClosedVolume ?? 0;
                    dto.contractSize = (double)_msg.Position?.TradeData?.LotSize;
                    dto.price = (double)_msg.Position?.Price;
                    dto.priceSL = _msg.Position.StopLoss;
                    dto.priceTP = _msg.Position.TakeProfit;

                    bool isOpen = (_msg.Position.PositionStatus == ProtoPositionStatus.PositionStatusOpen
                                && _msg.Position.PositionStatus != ProtoPositionStatus.PositionStatusError
                                && _msg.Position.PositionStatus != ProtoPositionStatus.PositionStatusClosed
                        );

                    PublishRabbitMq(dto, isOpen);
                }

                
            }
        
        
        }catch(Exception ex) { OnException(ex); }    

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
    private static void PublishRabbitMq(object dto, bool isOpen)
    {
        var jsonMessage = dto;
        string exchangeName = "ctrader-sandbox-exchange";
        var body = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(jsonMessage));
        string routingKey = string.Empty;
        /*routingKey = isOpen ? "open-deal"
                            : "close-deal";*/
        string queueName = isOpen ? "ztforex-newone-open-deals-queue"
                                  : "ztforex-newone-close-deals-queue";
        string queueNameII = "ztforex-ctrader-newone-deals-queue";
       
        channel.QueueDeclare(queueName, false, false, false, null);
        if (queueName == "ztforex-newone-open-deals-queue") { 
            channel.QueueBind(queue: queueName,
            exchange: exchangeName,
            routingKey: "open-deal");
            channel.BasicPublish(exchangeName, "open-deal", null, body);    
        }
        else if (queueName == "ztforex-newone-close-deals-queue")
        {
            channel.QueueBind(queue: queueName,
            exchange: exchangeName,
            routingKey: "close-deal");
            channel.BasicPublish(exchangeName, "close-deal", null, body);
        }
    }
    private static void PublishRabbitMqDeals(object dto)
    {
        var jsonMessage = dto;
        string exchangeName = "ctrader-sandbox-exchange";
        var body = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(jsonMessage));
        string routingKey = string.Empty;
        string queueName = "ztforex-newone-open-deals-queue";
       
        channel.QueueDeclare(queueName, false, false, false, null);
        channel.QueueBind(queue: queueName,
        exchange: exchangeName,
        routingKey: routingKey);
        channel.BasicPublish(exchangeName, routingKey, null, body);
    }

}