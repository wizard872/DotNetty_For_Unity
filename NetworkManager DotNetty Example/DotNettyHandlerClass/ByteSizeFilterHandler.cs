using System;
using System.Collections.Generic;
using DotNetty.Buffers;
using DotNetty.Codecs;
using DotNetty.Common.Utilities;
using DotNetty.Transport.Channels;
using UnityEngine;


public class ByteSizeFilterHandler : ByteToMessageDecoder
{
	//Decode 메소드.  이 함수에서 return; 을 하면 다음 데이터를 이어서 받을 수 있다.
	//Decode 의 'IByteBuffer msg' 는 ByteToMessageDecoder만의 고유 누적 버퍼이다.
    protected override void Decode(IChannelHandlerContext ctx, IByteBuffer msg, List<object> list)
    {
		//일종의 예시 핸들러이므로 수정해서 사용할 필요가 있습니다.
		//패킷 예시 =>  [int: headerSize][int: id][short: context][short: packetType][ byte[]: PacketData]
		
        //헤더부분(4 + 4 + 2 + 2 byte)도 다 못읽어왔을 경우! 12byte는 얼마든지 변경 가능.
        if (msg.ReadableBytes < 12)
        {
            return; //다시 되돌려서 패킷을 이어서 더 받아오게끔 하는 부분!
        }
		
        //앞으로 받아올 패킷크기를 불러온다! (받아오는 byteArray가  LittleEndian 방식임을 가정할때.)
        int packet_size = msg.GetIntLE(0); //msg 버퍼 객체의 0번 Index로부터 4byte만큼, LittleEndian 방식으로 Int값을 불러옴. 

		//패킷 조건 임의 지정 가능.
        if (65530 < packet_size || packet_size < 0)
        {
            Debug.Log("NetworkManager:Packet손상!! packet_size=" + packet_size + "\n");
            Debug.Log("NetworkManager:이상이 있으므로 이 패킷은 무시됩니다.");
            
			//채널 연결 종료.
            ctx.Channel.CloseAsync();
			
			//나머지 부분 이어서 받기.
            return;
        }

        //아직 버퍼에 데이터가 충분하지 않다면 다시 되돌려서 패킷을 이어서 더 받아오게끔 하는 부분!
        if (msg.ReadableBytes < packet_size + 12)
        {
            return; //다시 되돌려서 패킷을 이어서 더 받아오게끔 하는 부분!
        }
        //패킷이 뭉쳐서 데이터를 더 받아왔을 경우! 원래 받아야 할 부분만 받고, 나머지는 다시 되돌려서 뒤이어 오는 패킷과 합쳐서 받을 것!
        else if (msg.ReadableBytes > packet_size + 12)
        {
			//완성된 패킷만큼 데이터를 읽어온다.
            IByteBuffer perfect_packet = msg.ReadBytes(packet_size + 12);

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
			//완성된 패킷만큼 데이터를 읽어온다.
            IByteBuffer perfect_packet = msg.ReadBytes(packet_size + 12);

			//list에 추가해주면 자동으로 처리용 핸들러로 넘어간다.
            list.Add(perfect_packet);
        }
    } //Decode 메소드 종료 부분.
	
} //클래스 종료 부분.
