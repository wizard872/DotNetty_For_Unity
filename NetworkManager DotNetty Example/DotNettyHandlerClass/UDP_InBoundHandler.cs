using System;
using System.Net;
using DotNetty.Buffers;
using DotNetty.Common.Utilities;
using DotNetty.Transport.Channels;
using DotNetty.Transport.Channels.Sockets;
using UnityEngine;

class UDP_InBoundHandler : ChannelHandlerAdapter, IChannelHandler
{
	//각각, 채널 등록, 해제시에 불러와지는 Callback 메소드. 세부사항은 자바 Netty 프레임워크 동작방식 참조.
    public override void ChannelRegistered(IChannelHandlerContext context) { Debug.Log("ChannelRegistered"); }
    public override void ChannelUnregistered(IChannelHandlerContext context) { Debug.Log("ChannelUnregistered"); } 

	//UDP 통신이 가능해졌을 때 실행되는 Callback 메소드.
    void IChannelHandler.ChannelActive(IChannelHandlerContext context)
    {
        Debug.Log("NetworkManager:UDP ChannelActive! LocalAddress=" + context.Channel.LocalAddress);
		
		//context.Channel 은 NetworkManager 의 udpChannel 과 동일하다.
    }

	//UDP 통신이 불가능해졌을 때 실행되는 Callback 메소드.
    void IChannelHandler.ChannelInactive(IChannelHandlerContext context)
    {
        Debug.Log("NetworkManager:UDP ChannelInactive!");
		
		//context.Channel 은 NetworkManager 의 udpChannel 과 동일하다.
    }


    //UDP 데이터 수신시.
    public override void ChannelRead(IChannelHandlerContext context, object message)
    {
		//UDP 채널의 경우에는 DatagramPacket으로 받아온다.
        DatagramPacket msg = (DatagramPacket)message;

		//DatagramPacket 에서 IByteBuffer 데이터를 가져온다.
        IByteBuffer receive_data = msg.Content;
        
		//받아온 데이터 길이(receive_data.ReadableBytes) 만큼 byte[] 를 할당한다.
        byte[] recvData = new byte[receive_data.ReadableBytes];

		//receive_data 로부터  recvData 에다가 데이터를 기록한다. receive_data의 0번 offset부터 읽어와서 recvData에다가 기록함.
        receive_data.GetBytes(0, recvData);

		//받아온 데이터를 메인스레드와 통신하기 위한 목적으로 만들어둔 수신 Queue에 삽입.
		//멀티스레드에서 공유하는 Queue이므로, Locking 처리.  NetworkManager.locking 는 스레드 lock용 모니터 객체이다.
        lock (NetworkManager.locking)
        {
			//데이터 삽입.
            NetworkManager.packet_Queue.Enqueue(new Packet_Data(recvData));
        }

		//한번 할당받은 IByteBuffer 객체는 할당해제를 해줘야 한다. ReferenceCount가 0인 객체는 자동으로 반환되며, ReferenceCount가 0인 객체에 접근시 에러 발생!
        if (msg.ReferenceCount > 0)
            ReferenceCountUtil.Release(msg, msg.ReferenceCount);
    }

	//DotNetty 워커 스레드에서 예외 발생시 호출되는 Callback 메소드.
    public override void ExceptionCaught(IChannelHandlerContext context, Exception e)
    {
        Debug.Log(e);
		
		//예외가 발생한 채널의 연결을 종료...
        context.Channel.CloseAsync();
    }
}