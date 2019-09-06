using UnityEngine;

using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using System.Collections;
using System.Collections.Generic;

using DotNetty.Buffers;
using DotNetty.Handlers.Logging;
using DotNetty.Transport.Bootstrapping;
using DotNetty.Transport.Channels;
using DotNetty.Transport.Channels.Sockets;

public class NetworkManager : MonoBehaviour
{
    //싱글톤 패턴을 구현하기 위한 고정객체변수.
    public static NetworkManager ins;

    Thread networkClientThread;

    //네트워크 이벤트 처리용 부트스트랩.
    static Bootstrap tcpBootstrap;
    static Bootstrap udpBootstrap;

    //통신용 소켓채널.  소켓과 같다고 보면 된다.
    static IChannel tcpChannel;
    static IChannel udpChannel;

    //ByteBuf 재사용 Pool
    static PooledByteBufferAllocator alloc = PooledByteBufferAllocator.Default;

    //목적지 지정.
    static IPEndPoint serverAddress_TCP = null;
    static IPEndPoint serverAddress_UDP = null;

    //메인스레드 통신용 Queue 제어용 락 모니터 객체.
    public static object locking = new object();

    //메인스레드 통신용 Queue.
    public static Queue<Packet_Data> packet_Queue;

    void Awake()
    {
		//싱글톤 패턴을 위한 부분...?
        if (ins == null)
        {
            ins = this;
        }
        //혹시나 생성된 tcp_instance가 자기자신이 아니라면 이미 생성된 것이 존재한다는 것이므로.
        //나중에 생성된 객체를 다시 파괴해서 총 객체수가 언제나 1개가 되게끔 유지!
        else if (ins != this)
        {
            Destroy(gameObject);
        }
        //TCP 매니저 객체를 계속 유지!
        DontDestroyOnLoad(gameObject);

        //별도 쓰레드에서 동작하는 비동기 소켓 메소드에서 값을 수신했을때 결과값을 담아두는 Queue. 받아온 값을 메인 쓰레드에서도 활용할 수 있게끔 하는것이 목적이다
        packet_Queue = new Queue<Packet_Data>();
    }
    
    void Start()
    {
        //네트워크 클라이언트 스레드 초기화 & 시작.
        networkClientThread = new Thread(Unity_DotNettyClient);
        networkClientThread.Start();
    }

    void OnEnable()
    {
        //받은 데이터를 Queue에서 꺼내서 메인 스레드로 옮김!
        StartCoroutine(recvPacket());
    }

    void OnDisable()
    {
		//코루틴 종료.
        StopCoroutine(recvPacket());
    }

    //메인 스레드에서 동작하는 코루틴.
    IEnumerator recvPacket()
    {
        Debug.Log("start recvPacket()");
        WaitForSecondsRealtime recvRate = new WaitForSecondsRealtime(0.005f); //5ms 단위로 Queue 확인하여 메인스레드로 데이터를 가져옴.
        Packet_Data recvData = null;

        while (true)
        {
			//서로 다른 스레드가 Queue를 공유하고 있으므로, 동기화 제어.
            lock (locking)
            {
				//데이터가 있다면 가져옴.
                if (packet_Queue.Count > 0)
                {
                    recvData = packet_Queue.Dequeue();
                }
            }

			//가져온 데이터가 있다면 수신처리.
            if (recvData != null)
            {
                //패킷이 도착함.
                byte[] data = recvData.packet_data;
				
				//do Something. 이부분에, 다른 메소드 등을 넣어서, 패킷 수신시 처리등을 하면 된다.
				
                recvData = null;
            }
            yield return recvRate; //지정된 시간만큼 대기.
        }
    }


    //게임 서버로 접속 시도! 
    public async static void ConnectGameServer(string TYPE, string IP_OR_DOMAIN_VALUE, int PORT)
    {
        try
        {
			if (TYPE.Equals("ip"))
            {
				//TCP 입력된 주소, 포트번호로부터 Endpoint 지정.
                serverAddress_TCP = new IPEndPoint(IPAddress.Parse(IP_OR_DOMAIN_VALUE), PORT);
				
				//UDP 접속주소.
                serverAddress_UDP = new IPEndPoint(IPAddress.Parse(IP_OR_DOMAIN_VALUE), PORT); 
            }
            else if (TYPE.Equals("domain"))
            {
				//TCP 도메인 명으로 입력된 경우, DNS를 통해 IP주소로 파싱후 적용!
                serverAddress_TCP = new IPEndPoint(Dns.GetHostEntry(IP_OR_DOMAIN_VALUE).AddressList[0], PORT);
				
				//UDP 접속주소.
                serverAddress_UDP = new IPEndPoint(Dns.GetHostEntry(IP_OR_DOMAIN_VALUE).AddressList[0], PORT);
            }
            else
            {
                Debug.Log("Error: connect()의 첫번째 인자의 타입을 ip 또는 domain중 하나만 입력하십시오.");
				//잘못 입력하였으므로 종료.
				return;
            }			
			
			//소켓의 Connect 와 같은 부분. 비동기로 이루어지며 작업이 완료되면 tcpChannel 를 통해, 서버와 통신이 가능하다!
			tcpChannel = await tcpBootstrap.ConnectAsync(serverAddress_TCP);   
        }
        catch (Exception e)
        {
            Debug.Log("연결 시도중 에러! Error:" + e.ToString());
        }
    }    

    //UDP 접속 함수. 연결할 서버의 UDP 리스닝 포트번호를 입력함.
    public static void initUDPConnection(int setPort=-1)
    {
        try
        {
			//클라이언트의 UDP포트 바인딩 작업. (비동기로 실행됨)
            Task<IChannel> bindingTask = udpBootstrap.BindAsync( 0 );
			
			//바인딩 작업이 끝났다면 실행되는 델리게이트 함수.
            bindingTask.GetAwaiter().OnCompleted(() =>
            {
                try
                {
					//바인딩된 udpChannel를 결과로 받아옴.
                    udpChannel = bindingTask.Result;

                    //지정된 포트가 존재한다면 해당 포트로 연결 시도! 지정되있지 않다면 ConnectGameServer 에서 지정된 주소로 연결함.
                    if (setPort != -1)
                        serverAddress_UDP.Port = setPort;

					//서버의 UDP포트로 연결할 작업을 진행.
					//(이 과정이 완료되면 위의 바인딩때 지정된 0번포트에서, 운영체제에서 자동으로 할당된 포트로 UDP채널이 최종 바인딩 되고, 송수신 가능한 상태가 된다)
                    Task connectTask = udpChannel.ConnectAsync(serverAddress_UDP);
					
					//연결 작업이 완료되었다면.
                    connectTask.GetAwaiter().OnCompleted(() =>
                    {
						//완료된 ip주소, port값 출력.
                        Debug.Log("initUDPConnection Complete [" + serverAddress_UDP.Address.ToString()+":"+serverAddress_UDP.Port+"]");
                    });
                }
                catch (Exception e)
                {
                    Debug.Log("UDP Connect Error:" + e.ToString());
                }
            });
        }
        catch (Exception e)
        {
            Debug.Log("UDP Bind Error:" + e.ToString());
        }
    }
    
	
	
	
	
	//TCP 송신.
    public static void SendByte(byte[] send_data)
    {
		//tcpChannel 의 상태를 점검하여, 송신할 상황이 아니라면 종료.
        if (tcpChannel == null || tcpChannel.Active != true || tcpChannel.IsWritable != true)
        {
            return;
        }
		
        try
        {
			//IByteBuffer 는, 자바 Netty 의 ByteBuf 와 같은 역할.
			//보낼 데이터릐 길이만큼, alloc 에서 DirectBuffer를 할당받아서 가져옴.
            IByteBuffer data = alloc.DirectBuffer(send_data.Length);
			
			//받아온 byte[] 데이터를, 할당받아온 IByteBuffer에 기록함.
            data.WriteBytes(send_data);

			//tcpChannel 를 통해 해당 데이터를 송신!
            tcpChannel.WriteAndFlushAsync(data);
        }
        catch (Exception e)
        {
            Debug.Log("SendByte() Error:" + e.ToString());
        }
    }

	//UDP 송신.
    public static void udpSendByte(byte[] send_data)
    {
		//udpChannel 의 상태를 점검하여, 송신할 상황이 아니라면 종료.
        if (udpChannel == null || udpChannel.Active != true || udpChannel.IsWritable != true)
        {
            return;
        }
		
        try
        {
			//IByteBuffer 는, 자바 Netty 의 ByteBuf 와 같은 역할.
			//보낼 데이터릐 길이만큼, alloc 에서 DirectBuffer를 할당받아서 가져옴.
            IByteBuffer data = alloc.DirectBuffer(send_data.Length);
			
			//받아온 byte[] 데이터를, 할당받아온 IByteBuffer에 기록함.
            data.WriteBytes(send_data);

			//DotNetty에서 제공하는 DatagramPacket 객체를 꾸밈.
			//DatagramPacket 생성자 =>>  new DatagramPacket(보낼 데이터(IByteBuffer), 보낸곳의 로컬 주소정보(udpChannel.LocalAddress), 보낼 목적지 주소정보(udpChannel.RemoteAddress));
            DatagramPacket udp_data = new DatagramPacket(data, udpChannel.LocalAddress, udpChannel.RemoteAddress);

			//udpChannel 를 통해 해당 Datagram 데이터를 송신!
            udpChannel.WriteAndFlushAsync(udp_data);
        }
        catch (Exception e)
        {
            Debug.Log("udpSendByte() Error:" + e.ToString());
        }
    }

    //Unity_DotNetty 프레임워크 작업용 스레드.
    void Unity_DotNettyClient()
    {
        Debug.Log("NetworkManager:Init...");

        //이벤트 루프 그룹 설정. Java Netty프레임워크의 이벤트 루프 그룹.
        IEventLoopGroup tcp_workerGroup;
        IEventLoopGroup udp_workerGroup;

        //Client 용도이므로 각각 싱글스레드 할당!
        tcp_workerGroup = new MultithreadEventLoopGroup(1); //안의 숫자만큼 스레드가 생성된다. 여러개로 해도 된다.
        udp_workerGroup = new MultithreadEventLoopGroup(1); //서버로 사용할시 공백으로 하면 CPU 코어수 * 2 만큼의 스레드가 자동 할당됨.

        //네트워크 이벤트 처리용 부트스트랩.
        tcpBootstrap = new Bootstrap();
        udpBootstrap = new Bootstrap();

		//빌더 패턴을 통하여 세팅함.
        try
        {
			//TCP 세팅.
            tcpBootstrap
            .Group(tcp_workerGroup) //처리용 MultithreadEventLoopGroup 지정.

            .Channel<TcpSocketChannel>() //TCP 소켓채널 지정.  

            .Option(ChannelOption.Allocator, PooledByteBufferAllocator.Default) //IByteBuffer Pooling 객체이다. 메모리 절약용.

            .Option(ChannelOption.TcpNodelay, true) //빠른 반응속도를 위한 TCP 네이글 알고리즘 해제용.

            .Handler(new LoggingHandler()) //로깅용 핸들러.

			//실제로 수신처리를 위한 핸들러. 먼저 ByteSizeFilterHandler를 통해, 뭉쳐서 오거나 잘려서 오는 TCP 패킷의 길이를 가공하는 역할.
            .Handler(new ActionChannelInitializer<TcpSocketChannel>(channel =>
            {
                IChannelPipeline pipeline = channel.Pipeline;
				
				//먼저 ByteSizeFilterHandler 에서 데이터를 받아서 처리후, TCP_InBoundHandler로 데이터를 넘겨줍니다.				
                pipeline.AddLast("ByteSizeFilterHandler", new ByteSizeFilterHandler()); //패킷 길이 가공.
                pipeline.AddLast("TCP_InBoundHandler", new TCP_InBoundHandler()); //실제 TCP패킷 처리.
            }));

            Debug.Log("NetworkManager:DotNetty TCP_Init...Complete");


			//UDP 세팅.
            udpBootstrap
            .Group(udp_workerGroup) //처리용 MultithreadEventLoopGroup 지정.

            .Channel<SocketDatagramChannel>() //UDP 소켓채널 지정.

            .Option(ChannelOption.Allocator, PooledByteBufferAllocator.Default) //IByteBuffer Pooling 객체이다. 메모리 절약용.

			//실제로 수신처리를 위한 핸들러. UDP_InBoundHandler를 통해 UDP 패킷을 처리한다.
            .Handler(new ActionChannelInitializer<SocketDatagramChannel>(channel =>
            {
                IChannelPipeline pipeline = channel.Pipeline;

                pipeline.AddLast("UDP_InBoundHandler", new UDP_InBoundHandler()); //실제 UDP패킷 처리.
            }));

            Debug.Log("NetworkManager:DotNetty UDP_Init...Complete");

            Debug.Log("NetworkManager:DotNetty Initialize Complete");

            //블로킹 부분.
            while (true)
            {
                Thread.Sleep(3600);
            }
        }
        catch (Exception e)
        {
            Debug.Log(e);
        }
        finally
        {
			//최종 종료될 경우, 이벤트 루프 그룹의 자원들을 해제한다.
            tcp_workerGroup.ShutdownGracefullyAsync(TimeSpan.FromMilliseconds(100), TimeSpan.FromSeconds(1));
            udp_workerGroup.ShutdownGracefullyAsync(TimeSpan.FromMilliseconds(100), TimeSpan.FromSeconds(1));

            Debug.Log("NetworkManager:DotNetty ShutdownGracefully Complete.");
        }
    } //비동기 스레드 작업 끝.

} //NetworkManager 클래스 종료 부분.


//서브스레드, 메인스레드간 통신용 Queue 클래스. 
public class Packet_Data
{
    public byte[] packet_data;

	//공백 생성자.
    public Packet_Data() {}

	//byte[] 생성자.
    public Packet_Data(byte[] packet_data)
    {
        this.packet_data = packet_data;
    }
}
