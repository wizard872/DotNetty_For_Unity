using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using DotNetty.Buffers;
using DotNetty.Codecs;
using DotNetty.Common.Utilities;
using DotNetty.Transport.Channels;
using UnityEngine;


//일종의 예시 핸들러이므로 수정해서 사용할 필요가 있습니다.

public class ByteSizeFilterHandler : ByteToMessageDecoder
{
    //버퍼 재사용을 위한 버퍼Pool 할당을 위한 객체. 이로서 버퍼 생성, 삭제에 많은 비용을 들일 필요없이 재사용을 하여
    //자원 절약을 통해 성능 향상에 도움이 된다!
    PooledByteBufferAllocator alloc = PooledByteBufferAllocator.Default;

	//헤더의 크기를 받아오는 부분.
    byte[] header_size = new byte[4];    

	//Decode 메소드.  이 함수에서 return; 을 하면 다음 데이터를 이어서 받을 수 있다.
    protected override void Decode(IChannelHandlerContext ctx, IByteBuffer msg, List<object> list)
    {
        //헤더부분(4 + 4 + 2 + 2 byte)도 다 못읽어왔을 경우! 12byte는 얼마든지 변경 가능.
        if (msg.ReadableBytes < 12)
        {
            return; //다시 되돌려서 패킷을 이어서 더 받아오게끔 하는 부분!
        }

        //패킷정보가 담긴 헤더데이터를 다 받아왔을 경우!
        msg.GetBytes(0, header_size);

        //앞으로 받아올 패킷크기를 불러온다! (받아오는 byteArray가  LittleEndian 방식임을 가정할때. 만약 BigEndian방식이라면 Array.Reverse 등으로 바꿔주어야 한다.)
        int packet_size = BitConverter.ToInt32(header_size, 0); 

		//패킷 조건 임의 지정 가능.
        if (65530 < packet_size || packet_size < 0)
        {
            Debug.Log("NetworkManager:Packet손상!! packet_size=" + packet_size + "\n");
            Debug.Log("NetworkManager:이상이 있으므로 이 패킷은 무시됩니다.");
            
			//채널 연결 종료.
            ctx.Channel.CloseAsync();
			
			//채널 연결 종료가 완료되었다면.
            ctx.Channel.CloseCompletion.GetAwaiter().OnCompleted(() =>
            {
                //사용된 메모리를 반환한다!
                if (msg.ReferenceCount > 0)
                    ReferenceCountUtil.Release(msg, msg.ReferenceCount);
            });
			//나머지 부분 이어서 받기.
            return;
        }

        //packet크기가 0인 경우, 부하가 커서 못받아온것이므로 다음번에 받아오게끔 패스할것.
        if (packet_size == 0)
            return;



        //아직 버퍼에 데이터가 충분하지 않다면 다시 되돌려서 패킷을 이어서 더 받아오게끔 하는 부분!
        if (msg.ReadableBytes < packet_size + 12)
        {
            return; //다시 되돌려서 패킷을 이어서 더 받아오게끔 하는 부분!
        }
        //패킷이 뭉쳐서 데이터를 더 받아왔을 경우! 원래 받아야 할 부분만 받고, 나머지는 다시 되돌려서 뒤이어 오는 패킷과 합쳐서 받을 것!
        else if (msg.ReadableBytes > packet_size + 12)
        {
            IByteBuffer perfect_packet = alloc.DirectBuffer(packet_size + 12);

            perfect_packet.WriteBytes(msg, 0, packet_size + 12);

            msg.ReadBytes(perfect_packet, 0, packet_size + 12);

			//일단 완성된 패킷은 처리용 핸들러로 넘긴다.
            ctx.FireChannelRead(perfect_packet);

			//읽은 만큼의 readIndex 만큼 이미 읽은 부분을 제거.
            msg.DiscardReadBytes();

			//그리고 다음 부분을 마저 받아오도록 한다.
            return;
        }
        //크기가 딱 맞게 온, 정확한 데이터일 경우.
        else
        {
            IByteBuffer perfect_packet = alloc.DirectBuffer(/*packet_size + 12*/ packet_size + 12);

            msg.GetBytes(0, perfect_packet);

			//list에 추가해주면 자동으로 처리용 핸들러로 넘어간다.
            list.Add(perfect_packet);

			//msg객체의 readIndex, writeIndex를  각각 0, 0으로 초기화해준다.
            msg.Clear();            
        }
    } //Decode 메소드 종료 부분.
	
} //클래스 종료 부분.
