DotNetty는 Java 진영에서 많이 쓰이고 있는, EventDriven 방식의 고성능 비동기 네트워크 프레임워크인 Netty를 
C# .NET으로 포팅한 것입니다.
DotNetty를 Unity GameEngine에서 사용하기 위해 DotNetty 리포지토리에서 코드 일부분을 수정하였습니다.

DotNetty 리포지토리 -> https://github.com/Azure/DotNetty



Unity 2019.2.10f 에서 테스트 되었습니다.
(테스트 설정-> IL2CPP 빌드, Release 모드, API Compatibility .NET Standard 2.0 & .NET 4.x)

Android, iOS, Standalone Windows & MacOS 빌드에서 TCP, UDP통신이 동작하는것을 확인하였습니다.


사용법

DotNetty-0.6.0_forUnity Plugins 폴더를 유니티 프로젝트의 Assets/Plugins 폴더로 복사하고나서 사용하시면 됩니다.

