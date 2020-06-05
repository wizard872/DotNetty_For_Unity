using System;
using System.Text;
using DotNetty.Buffers;
using DotNetty.Common.Utilities;
using DotNetty.Transport.Channels;
using UnityEngine;


//일종의 예시 핸들러이므로 수정해서 사용할 필요가 있습니다.
//패킷 예시 =>  [int: headerSize][int: id][short: context][short: packetType] [ byte[]: PacketData]
//헤더는 12byte를 사용한다고 가정.

public class TCP_InBoundHandler : ByteToMessageDecoder, IChannelHandler
{
	//각각, 채널 등록, 해제시에 불러와지는 Callback 메소드. 세부사항은 자바 Netty 프레임워크 동작방식 참조.
    public override void ChannelRegistered(IChannelHandlerContext context) { Debug.Log("ChannelRegistered"); }
    public override void ChannelUnregistered(IChannelHandlerContext context) { Debug.Log("ChannelUnregistered"); }  


	//TCP 통신이 가능해졌을 때 실행되는 Callback 메소드. 연결에 성공하였을 때의 처리를 하면 된다.
    void IChannelHandler.ChannelActive(IChannelHandlerContext context)
    {        
        Debug.Log("NetworkManager:TCP ChannelActive! LocalAddress=" + context.Channel.LocalAddress);

		//context.Channel 은 NetworkManager 의 tcpChannel 과 동일하다.
    }

	//TCP 통신이 불가능해졌을 때 실행되는 Callback 메소드. 연결이 끊겼을때의 처리를 하면 된다.
    void IChannelHandler.ChannelInactive(IChannelHandlerContext context)
    {
        Debug.Log("NetworkManager:TCP ChannelInactive!");
        
		//context.Channel 은 NetworkManager 의 tcpChannel 과 동일하다.
		
		//Channel.CloseAsync(); 를 호출하였을때도 ChannelInactive 부분이 호출된다. 
    }
    

    //TCP 데이터 수신시.
    //Decode(IChannelHandlerContext ctx, IByteBuffer msg, List<object> list) 에서,
    //IByteBuffer msg 는 ByteToMessageDecoder가 내부적으로 가지고 있는 누적버퍼이다.
    protected override void Decode(IChannelHandlerContext ctx, IByteBuffer msg, List<object> list)
    {
	    //헤더부분(4 + 4 + 2 + 2 byte)도 다 못읽어왔을 경우! 12byte는 얼마든지 변경 가능.
	    if (msg.ReadableBytes < 12)
	    {
		    return; //다시 되돌려서 패킷을 이어서 더 받아오게끔 하는 부분!
	    }

	    //readerIndex 저장.
	    msg.MarkReaderIndex();
	    
		//이 예제에서 packet_size는, 헤더12byte를 제외한, 순수한 패킷 자체의 데이터크기를 나타낸다.
		
	    //앞으로 받아올 패킷크기를 불러온다! int(4byte) 만큼 읽었으므로 readerIndex 0 -> 4로 증가됨.
	    int packet_size = msg.ReadIntLE(); //msg 버퍼 객체로부터 4byte만큼, LittleEndian 방식으로 Int값을 불러옴. 

	    //isAvailablePacketSize() 함수로 패킷 크기 체크.
	    if (!isAvailablePacketSize(packet_size))
	    {
		    //비정상적인 데이터가 도달하였다면.
		    Debug.Log("[TCP_InBoundHandler] Packet손상!! packet_size = " + packet_size + "");

		    //연결 종료
		    ctx.Channel.CloseAsync(); //채널이 닫힌다면 msg는 release 된다.
		    return;
	    }

	    //아직 버퍼에 데이터가 충분하지 않다면 다시 되돌려서 패킷을 이어서 더 받아오게끔 하는 부분!
	    if (msg.ReadableBytes < packet_size + 8) //<- 이미 4byte만큼 읽었으므로 헤더크기 12에서 4를 뺀값.
	    {
		    //정상적으로 데이터를 다 읽은것이 아니므로 아까 MarkReaderIndex로 저장했던 0값으로 readerIndex 4 -> 0로 초기화함.
		    msg.ResetReaderIndex();
		    return; //다시 되돌려서 패킷을 이어서 더 받아오게끔 하는 부분!
	    }
	    //패킷을 완성할만큼 충분한 데이터를 받아온 경우
	    else
	    {
		    //패킷 완성후, 다음 데이터가 없는지 체크
		    if (!isCompleteSizePacket(packet_size, ctx, msg))
		    {
			    //다음 데이터가 없다면 다시 이어서 받는다.
			    return;
		    }
	    }
    }
	
    
    //패킷 완성후, 다음 데이터가 없는지 체크하는 함수.
    //패킷 완성후, 다시 자기자신을 호출하여 다음 데이터가 없을때까지 계속 자기자신을 호출한다.
    bool isCompleteSizePacket(int packet_size, IChannelHandlerContext ctx, IByteBuffer msg)
    {
        //아직 버퍼에 데이터가 충분하지 않다면 다시 되돌려서 패킷을 이어서 더 받아오게끔 하는 부분!
        if (msg.ReadableBytes < packet_size + 8)
        {
            //정상적으로 데이터를 다 읽은것이 아니므로 아까 MarkReaderIndex로 저장했던 0값으로 readerIndex 4 -> 0로 초기화함.
            msg.ResetReaderIndex();
            return false; //다시 되돌려서 패킷을 이어서 더 받아오게끔 하는 부분!
        }
        //패킷이 뭉쳐서 데이터를 더 받아왔을 경우! 원래 받아야 할 부분만 받고, 나머지는 다시 되돌려서 뒤이어 오는 패킷과 합쳐서 받을 것!
        else if (msg.ReadableBytes > packet_size + 8)
        {
	        //받아온 데이터.
            byte[] packet_data = new byte[msg.ReadableBytes];
            msg.ReadBytes(packet_data, 0, msg.ReadableBytes);

            //버퍼의 현재 용량에서 추가로 기록가능한 영역이 10KB 이하라면,
            //이미 읽은 분량만큼은 필요없으므로 Index 0의 위치로 데이터를 옮겨 추가로 기록가능한 영역을 늘린다.
            if (msg.Capacity - msg.WriterIndex < 10000)
                msg.DiscardReadBytes();

            
            
            
            //받아온 데이터를 메인스레드와 통신하기 위한 목적으로 만들어둔 수신 Queue에 삽입.
            //멀티스레드에서 공유하는 Queue이므로, Locking 처리.  NetworkManager.locking 는 스레드 lock용 모니터 객체이다.
            lock (NetworkManager.locking)
            {
	            //받아온 데이터를 수신Queue에 삽입하는 부분.

	            //데이터 삽입.
	            NetworkManager.packet_Queue.Enqueue( new Packet_Data(packet_data) );
            }



            //패킷 완성후 남은 데이터가 헤더크기인 12byte보다 작을 경우.
            if (msg.ReadableBytes < 12)
            {
                return false;
            }
            else
            {
                msg.MarkReaderIndex();
                int amountSize = msg.ReadIntLE();

                //비정상적인 데이터가 도달하였다면.
                if (!isAvailablePacketSize(amountSize))
                {
                    //연결 종료
                    ctx.CloseAsync();  //채널이 닫힌다면 msg는 release 된다.
                    return false;
                }

                return isCompleteSizePacket(amountSize, ctx, msg); //재귀함수 처리
                //패킷 완성후, 남은데이터가 없을때까지 계속 반복한다.
            }
        }
        else //정확한 데이터일 경우. [msg.ReadableBytes > packet_size + 8] 또는 [msg.readableBytes() + 4 == packet_size + 12]
        {
	        //받아온 데이터.
	        byte[] packet_data = new byte[msg.ReadableBytes];
	        msg.ReadBytes(packet_data, 0, msg.ReadableBytes);

            //버퍼에 읽어와야 할 데이터가 남아있지 않으므로 readerIndex, writerIndex를 각각 0으로 초기화 한다.
            //내부 버퍼 데이터를 변경하지 않고 index값만 변경하므로 자원을 적게 소모한다.
            msg.Clear();


            //받아온 데이터를 메인스레드와 통신하기 위한 목적으로 만들어둔 수신 Queue에 삽입.
            //멀티스레드에서 공유하는 Queue이므로, Locking 처리.  NetworkManager.locking 는 스레드 lock용 모니터 객체이다.
            lock (NetworkManager.locking)
            {
	            //받아온 데이터를 수신Queue에 삽입하는 부분.

	            //데이터 삽입.
	            NetworkManager.packet_Queue.Enqueue( new Packet_Data(packet_data) );
            }

            //다시 되돌려서 패킷을 이어서 더 받아오게끔 하는 부분!
            return false;
        }
    }
    
    bool isAvailablePacketSize(int packet_size)
    {
	    //비정상적인 패킷이라면 false
	    if (0 >= packet_size || packet_size > 65535)
		    return false;

	    //정상적인 패킷이라면 true
	    else
		    return true;
    }
    
    
	//DotNetty 워커 스레드에서 예외 발생시 호출되는 Callback 메소드.
    public override void ExceptionCaught(IChannelHandlerContext context, Exception e)
    {
        Debug.Log(e.Message+e.StackTrace);
		
		//예외가 발생한 채널의 연결을 종료...
        context.Channel.CloseAsync();
    }
}
