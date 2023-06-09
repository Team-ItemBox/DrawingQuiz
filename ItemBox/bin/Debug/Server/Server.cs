﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Net;
using System.Net.Sockets;
using System.Collections;
using System.Threading;
using System.Diagnostics;
using System.IO;
using socketProtocol_Library;
using MySql.Data.MySqlClient;

namespace Server
{
    public partial class Server : Form
    {
        TcpListener server = null;
        TcpClient clientSocket = null;
        NetworkStream stream = default(NetworkStream);

        static int counter = 0;
        private byte[] sendbuffer = new byte[1024 * 4];
        private byte[] readbuffer = new byte[1024 * 4];

        public List<TcpClient> clients = new List<TcpClient>();

        //클라이언트 리스트 < TcpClient, 닉네임>
        public Dictionary<TcpClient, string> clientList = new Dictionary<TcpClient, string>();

        //클라이언트 리스트 < TcpClient, 클라이언트 번호>
        public Dictionary<TcpClient, int> clientNum = new Dictionary<TcpClient, int>();

        //클라이언트 리스트 <클라이언트 번호, 점수>
        public Dictionary<int, int> clientScore = new Dictionary<int, int>();

        public ArrayList nickNameList = new ArrayList();
        FileStream fileStrm = null;
        int countRead = 0;
        int turnClientNum = 0;

        static String strConn = "Server=13.209.111.12;Port=59876;Database=itemBox_DB;Uid=root;Pwd=9999";
        MySqlConnection conn = new MySqlConnection(strConn);
        MySqlCommand cmd;

        private void resetBuffer(byte[] buffer) { for (int i = 0; i < 1024 * 4; i++) buffer[i] = 0; }

        public Server()
        {
            InitializeComponent();

            //서버 시작
            Thread thStart = new Thread(InitSocket);
            thStart.IsBackground = true;
            thStart.Start();
        }



        private void InitSocket()
        {
            server = new TcpListener(IPAddress.Any, 9999);
            clientSocket = default(TcpClient);
            server.Start();
            DisplayText(">> Server Start <<");

            while (true)
            { //클라이언트 요청 받으면 소켓과 닉네임 저장
                try
                {
                    clientSocket = server.AcceptTcpClient();
                    clientNum.Add(clientSocket, counter);
                    fileStrm = new FileStream(Environment.CurrentDirectory + "\\Server\\bin\\Debug\\" + counter.ToString() + ".jpg", FileMode.Create, FileAccess.Write);
                    counter++;  //클라이언트 수++
                    DisplayText(">> Accept connection from client");

                    // 요청받은 클라이언트의 닉네임 패킷으로 받아 저장
                    ClientProfile clientProfile = new ClientProfile();
                    stream = clientSocket.GetStream();
                    int bytes = stream.Read(this.readbuffer, 0, this.readbuffer.Length);
                    string nickName = null;
                    Packet packet = (Packet)Packet.Deserialize(this.readbuffer);
                    clientProfile = (ClientProfile)Packet.Deserialize(this.readbuffer);
                    nickName = clientProfile.nickName;
                    clientList.Add(clientSocket, nickName);    //클라이언트 리스트에 새로 접속한 클라이언트 소켓과 닉네임 저장
                    clients.Add(clientSocket);
                    nickNameList.Add(nickName);
                    clientScore.Add(counter - 1, 0);

                    // 새로 접속한 클라이언트에게 클라이언트 넘버 및 기존 닉네임들 전송   
                    Init init = new Init();
                    init.clinetNumber = clientNum[clientSocket];
                    init.Type = (int)PacketType.초기화;
                    for (int i = 0; i <= clientNum[clientSocket]; i++)
                        init.nickNameList[i] = (string)nickNameList[i];
                    Packet.Serialize(init).CopyTo(this.sendbuffer, 0);
                    stream.Write(this.sendbuffer, 0, this.sendbuffer.Length);
                    stream.Flush();
                    resetBuffer(sendbuffer);

                    // (라벨 추가 위해) 닉네임 추가
                    foreach (var pair in clientList)
                    {
                        TcpClient client = pair.Key as TcpClient;

                        NickName nick_name = new NickName();
                        nick_name.client_sum = counter; //추가된 클라이언트수
                        nick_name.Type = (int)PacketType.닉네임;
                        for (int i = 0; i <= clientNum[clientSocket]; i++)
                            nick_name.nickNameList[i] = (string)nickNameList[i];

                        NetworkStream stream = client.GetStream();
                        Packet.Serialize(nick_name).CopyTo(this.sendbuffer, 0);

                        stream.Write(this.sendbuffer, 0, this.sendbuffer.Length);
                        this.stream.Flush();
                        resetBuffer(sendbuffer);
                    }

                    // 모두에게 전송
                    SendMessageAll(nickName + "님이 접속하셨습니다.", "", false);   //서버한테
                    //handleClient클래스는 클라이언트들을 다루는 클래스
                    handleClient h_client = new handleClient();
                    h_client.OnRequest += new handleClient.ClientRequestHandler(this.listenRequest);  //클라이언트 요청 이벤트 핸들러 추가.
                    h_client.OnDisconnected += new handleClient.DisconnectedHandler(h_client_OnDisconnected);   //연결 끊겼을 때
                    h_client.startClient(clientSocket, clientList, clientNum);
                }
                catch (SocketException se)
                {
                    Trace.WriteLine(string.Format("InitSocket - SocketException : {0}", se.Message));
                    break;
                }
                catch (Exception ex)
                {
                    Trace.WriteLine(string.Format("InitSocket - Exception : {0}", ex.Message));
                    break;
                }
            }

            clientSocket.Close();
            server.Stop();
        }

        void h_client_OnDisconnected(TcpClient clientSocket)
        {   //클라이언트의 연결이 끊긴 경우, 해당 클라이언트가 리스트에 속해있었다면 리스트에서 해당 튜플 제거
            MessageBox.Show(clientList[clientSocket] + " client Disconnected");
            if (clientList.ContainsKey(clientSocket))
                clientList.Remove(clientSocket);
        }

        private void listenRequest(int requestType, byte[] readBuffer, int cli_num)
        {   //클라이언트들이 전송하는 패킷 관리
            switch (requestType)
            {
                case (int)PacketType.메세지:
                    {
                        ClientMessage clientMessage = new ClientMessage();
                        clientMessage = (ClientMessage)Packet.Deserialize(readBuffer);
                        msgReceived(clientMessage.Message, clientMessage.nickName);
                        break;
                    }
                case (int)PacketType.프로필:
                    {
                        ClientProfile clientProfile = new ClientProfile();
                        clientProfile = (ClientProfile)Packet.Deserialize(readBuffer);
                        profileReceived(clientProfile.nickName, clientProfile.buffer, clientProfile.count, cli_num);
                        break;
                    }

                case (int)PacketType.게임상태:
                    {
                        GameState statePacket = new GameState();
                        statePacket = (GameState)Packet.Deserialize(readBuffer);
                        stateReceived(statePacket.gameStart);
                        break;
                    }
                case (int)PacketType.랭킹:
                    {
                        Ranking rank = new Ranking();
                        rank = (Ranking)Packet.Deserialize(readBuffer);
                        SendScoreAll();
                        break;
                    }
                case (int)PacketType.정답:
                    {
                        TurnChange turnPacket = new TurnChange();
                        turnPacket = (TurnChange)Packet.Deserialize(readBuffer);
                        DisplayText("Turn Change" + turnPacket.turn.ToString());
                        answerReceived(turnPacket.turn, turnPacket.score, turnPacket.message, turnPacket.clientNum);

                        break;
                    }

                case (int)PacketType.그림정보:
                    {
                        ClientDrawingInfo drawinfo = new ClientDrawingInfo();
                        drawinfo = (ClientDrawingInfo)Packet.Deserialize(readBuffer);
                        DisplayText(drawinfo.clientNum + "의 그림 도착");
                        fileStrm = new FileStream(Environment.CurrentDirectory + "\\Server\\bin\\Debug\\" + drawinfo.clientNum + "turn.jpg"
                            , FileMode.Create, FileAccess.Write);
                        break;
                    }
                case (int)PacketType.그림:
                    {
                        ClientDrawing draw = new ClientDrawing();
                        draw = (ClientDrawing)Packet.Deserialize(readBuffer);
                        drawReceived(draw.buffer, draw.count, draw.clientNum);
                        break;
                    }
            }
        }

        private void drawReceived(byte[] buffer, int count, int cli_num)
        {
            //그림 받음
            fileStrm.Write(buffer, 0, buffer.Length);
            countRead++;

            if (countRead == count)
            {
                fileStrm.Close();
                fileStrm = null;
                countRead = 0;
                //모두에게 그림 보냄
                SendDrawAll(cli_num);
            }
        }

        private void msgReceived(string message, string nickName)
        {   //클라이언트로부터 채팅 메시지를 받은 경우
            string displayMessage = "From client > " + nickName + " : " + message;
            DisplayText(displayMessage);    //서버창에 메세지 표시
            SendMessageAll(message, nickName, true);   //모든 클라이언트들에게 채팅 보냄
        }

        private void profileReceived(string nickName, byte[] buffer, int count, int cli_num)
        {   //클라이언트로부터 프로필 받아서 모두에게 전달
            string fileName = null;
            fileStrm.Write(buffer, 0, buffer.Length);
            countRead++;

            if (countRead == count)
            {
                fileName = fileStrm.Name;
                fileStrm.Close();
                fileStrm = null;
                countRead = 0;
            }

            //모두에게 프로필 전송
            if (fileName != null)
                SendProfileAll(nickName, cli_num);
        }

        private void stateReceived(bool state)
        {// 전달받은 게임상태 모든 클라이언트들에게 알리기 (게임상태 패킷 전송)
            foreach (var pair in clientList)
            {
                TcpClient client = pair.Key as TcpClient;
                GameState statePacket = new GameState();
                statePacket.Type = (int)PacketType.게임상태;

                statePacket.gameStart = state;
                if (state)         // 게임 시작했을 때
                    statePacket.message = "*** 게임을 시작합니다.   ***";
                else
                    statePacket.message = "*** 게임을 종료합니다.   ***";
                NetworkStream stream = client.GetStream();

                Packet.Serialize(statePacket).CopyTo(this.sendbuffer, 0);
                stream.Write(this.sendbuffer, 0, this.sendbuffer.Length);
                this.stream.Flush();
                resetBuffer(sendbuffer);
            }

            //다음 턴 준비 후 턴 정보 모두에게 전송
            SendTurnAll(turnClientNum, "", state);
        }

        private void answerReceived(int client, int score, string message, int clientNum)
        {//정답 맞추면 점수부여, 차례 변경, 다음 턴 준비
            clientScore[clientNum] += score;
            turnClientNum = client + 1;                //차례 변경
            if (turnClientNum == counter)
                turnClientNum = 0;
            SendTurnAll(turnClientNum, message, true); //다음 턴 준비 후 턴 정보 모두에게 전송
        }

        public void SendTurnAll(int turn, string message, bool state)
        {//다음 턴 준비 후 턴 정보 모두에게 전송
            if (state)
            {
                //다음 차례로 변경 및 다음 제시어 준비
                TurnChange turnPacket = new TurnChange();
                turnPacket.Type = (int)PacketType.턴;

                turnPacket.turn = turn;
                if (message != "")
                    turnPacket.message = message + "\r\n";

                turnPacket.message += "*** " + nickNameList[turn] + "님의 차례입니다. ***";



                string question = "";

                try
                {
                    conn = new MySqlConnection();
                    conn.ConnectionString = strConn;

                    conn.Open();

                    //제시어 담고 있는 데이터베이스에서 랜덤으로 하나 가져오기
                    string query = "select distinct(question) from game order by rand() limit 1";
                    cmd = conn.CreateCommand();
                    cmd.CommandText = query;

                    MySqlDataReader reader = cmd.ExecuteReader(); ;
                    while (reader.Read())
                    {
                        question = (reader["question"] + "");
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Error" + ex);
                }
                finally
                {
                    conn.Close();
                }

                turnPacket.Word = question;

                for (int i = 0; i < counter; i++)
                    turnPacket.clientscore[i] = clientScore[i];

                foreach (var pair in clientList)
                {
                    TcpClient client = pair.Key as TcpClient;

                    NetworkStream stream = client.GetStream();
                    Packet.Serialize(turnPacket).CopyTo(this.sendbuffer, 0);

                    stream.Write(this.sendbuffer, 0, this.sendbuffer.Length);
                    this.stream.Flush();
                    resetBuffer(sendbuffer);
                }
            }
        }
        public void SendScoreAll()
        {
            foreach (var pair in clientList)
            {
                TcpClient client = pair.Key as TcpClient;
                Ranking rank = new Ranking();
                rank.Type = (int)PacketType.랭킹;
                rank.clientSum = counter;

                for (int i = 0; i < counter; i++)   // 스코어 세팅
                {
                    rank.clientscore[i] = clientScore[i];            // 각 클라이언트의 점수를 랭킹 객체에 저장
                    rank.clientNickname[i] = nickNameList[i].ToString();   // 각 클라이언트의 닉네임을 랭킹 객체에 저장
                }

                NetworkStream stream = client.GetStream();   // 클라이언트의 네트워크 스트림 얻기
                Packet.Serialize(rank).CopyTo(this.sendbuffer, 0);   

                stream.Write(this.sendbuffer, 0, this.sendbuffer.Length);   // sendbuffer의 내용을 클라이언트로 전송
                this.stream.Flush(); 
                resetBuffer(sendbuffer);  
            }
        }


        public void SendMessageAll(string message, string nickName, bool flag)
        {
            // 모든 클라이언트 리스트를 탐색
            foreach (var pair in clientList)
            {
                TcpClient client = pair.Key as TcpClient;
                ClientMessage clientMessage = new ClientMessage();
                clientMessage.Type = (int)PacketType.메세지;

                NetworkStream stream = client.GetStream();

                if (flag) 
                {
                    clientMessage.Message = "[ " + nickName + " ] " + message;
                    Packet.Serialize(clientMessage).CopyTo(this.sendbuffer, 0);
                }
                else 
                {
                    clientMessage.Message = message;
                    Packet.Serialize(clientMessage).CopyTo(this.sendbuffer, 0);
                }

                // 메세지 전송
                stream.Write(this.sendbuffer, 0, this.sendbuffer.Length);
                stream.Flush();
                resetBuffer(sendbuffer);
            }
        }

        public void SendDrawAll(int cli_num)
        {
            // 모든 클라이언트 리스트를 탐색
            string fileName = null;
            for (int i = 0; i < clients.Count; i++)
            {
                if (i != cli_num)
                {
                    ClientDrawing draw = new ClientDrawing();
                    ClientDrawingInfo drawInfo = new ClientDrawingInfo();

                    byte[] sendBuffer = new byte[1024 * 4];

                    NetworkStream stream = clients[i].GetStream();

                    // 그림 정보보내기
                    drawInfo.Type = (int)PacketType.그림정보;
                    drawInfo.clientNum = cli_num; // 누가 보낸 그림인지
                    Packet.Serialize(drawInfo).CopyTo(sendBuffer, 0);
                    stream.Write(sendBuffer, 0, sendBuffer.Length);
                    stream.Flush();

                    resetBuffer(sendBuffer);

                    // 그림 보내기
                    fileName = Environment.CurrentDirectory + "\\Server\\bin\\Debug\\" + cli_num + "turn.jpg";
                    draw.Type = (int)PacketType.그림;

                    FileInfo file = new FileInfo(fileName);
                    FileStream fileStrm = new FileStream(file.FullName, FileMode.Open, FileAccess.Read);
                    BinaryReader reader = new BinaryReader(fileStrm);
                    int count = (int)(file.Length) / (1024) + 1;

                    for (int j = 0; j < count; j++)
                    {
                        draw.buffer = reader.ReadBytes(1024);
                        draw.count = count;
                        draw.clientNum = cli_num;

                        Packet.Serialize(draw).CopyTo(sendBuffer, 0);
                        stream.Write(sendBuffer, 0, sendBuffer.Length);
                        stream.Flush();

                        resetBuffer(sendBuffer);
                    }
                    fileStrm.Close();
                }
            }
        }

        public void SendProfileAll(string nickName, int cli_num)
        {
            string fileName = null;
            foreach (var pair in clientList)
            {
                TcpClient client = pair.Key as TcpClient;
                ClientProfile clientProfile = new ClientProfile();
                ClientProfileInfo profileInfo = new ClientProfileInfo();

                // 프로필 정보와 프로필 타입 설정
                profileInfo.Type = (int)PacketType.프로필정보;
                clientProfile.Type = (int)PacketType.프로필;

                NetworkStream stream = client.GetStream();   // 클라이언트의 네트워크 스트림 얻기

                profileInfo.clientNum = cli_num;   // 프로필 정보에 클라이언트 번호 설정
                fileName = Environment.CurrentDirectory + "\\Server\\bin\\Debug\\" + cli_num.ToString() + ".jpg";

                // 프로필 정보를 직렬화하여 sendbuffer에 복사 후 클라이언트로 전송
                Packet.Serialize(profileInfo).CopyTo(this.sendbuffer, 0);
                stream.Write(this.sendbuffer, 0, this.sendbuffer.Length);
                stream.Flush();

                resetBuffer(sendbuffer);   // sendbuffer 초기화

                FileInfo file = new FileInfo(fileName);
                FileStream fileStrm = new FileStream(file.FullName, FileMode.Open, FileAccess.Read);
                BinaryReader reader = new BinaryReader(fileStrm);
                int count = (int)(file.Length) / (1024) + 1;

                // 파일을 1024바이트씩 읽어서 클라이언트 프로필에 저장하고 전송
                for (int j = 0; j < count; j++)
                {
                    clientProfile.buffer = reader.ReadBytes(1024);
                    clientProfile.count = count;
                    clientProfile.nickName = nickName;
                    clientProfile.Type = (int)PacketType.프로필;
                    clientProfile.clientNum = cli_num;

                    Packet.Serialize(clientProfile).CopyTo(this.sendbuffer, 0);
                    stream.Write(this.sendbuffer, 0, this.sendbuffer.Length);
                    stream.Flush();

                    resetBuffer(sendbuffer);   // sendbuffer 초기화
                }
                fileStrm.Close();

                if (clientNum[client] == cli_num)
                {
                    for (int i = 0; i < cli_num; i++)
                    {
                        profileInfo.clientNum = i;   // 프로필 정보에 클라이언트 번호 설정
                        fileName = Environment.CurrentDirectory + "\\Server\\bin\\Debug\\" + i.ToString() + ".jpg";

                        // 프로필 정보를 직렬화하여 sendbuffer에 복사 후 클라이언트로 전송
                        Packet.Serialize(profileInfo).CopyTo(this.sendbuffer, 0);
                        stream.Write(this.sendbuffer, 0, this.sendbuffer.Length);
                        stream.Flush();

                        // sendbuffer 초기화
                        for (int j = 0; j < 1024 * 4; j++)
                            sendbuffer[j] = 0;

                        file = new FileInfo(fileName);
                        fileStrm = new FileStream(file.FullName, FileMode.Open, FileAccess.Read);
                        reader = new BinaryReader(fileStrm);
                        count = (int)(file.Length) / (1024) + 1;

                        // 파일을 1024바이트씩 읽어서 클라이언트 프로필에 저장하고 전송
                        for (int j = 0; j < count; j++)
                        {
                            clientProfile.buffer = reader.ReadBytes(1024);
                            clientProfile.count = count;
                            clientProfile.nickName = nickName;
                            clientProfile.Type = (int)PacketType.프로필;
                            clientProfile.clientNum = i;

                            Packet.Serialize(clientProfile).CopyTo(this.sendbuffer, 0);
                            stream.Write(this.sendbuffer, 0, this.sendbuffer.Length);
                            stream.Flush();

                            resetBuffer(sendbuffer);   // sendbuffer 초기화
                        }
                        fileStrm.Close();
                    }
                }
            }
        }

        private void DisplayText(string text) // 서버의 텍스트 박스에 추가
        {
            this.Invoke(new MethodInvoker(delegate ()
            {
                richTextBox1.AppendText(text + "\r\n");
                richTextBox1.Focus();
                richTextBox1.ScrollToCaret();
            }));
        }

        class handleClient // 클라이언트를 다루기 위한 클래스 -> 메세지 저장. 전송
        {
            TcpClient clientSocket = null;
            public Dictionary<TcpClient, string> clientList = null;
            public Dictionary<TcpClient, int> clientNum = null;
            private byte[] sendbuffer = new byte[1024 * 4];
            private byte[] readbuffer = new byte[1024 * 4];

            public void startClient(TcpClient clientSocket, Dictionary<TcpClient, string> clientList, Dictionary<TcpClient, int> clientNum)
            {
                this.clientSocket = clientSocket;
                this.clientList = clientList;
                this.clientNum = clientNum;

                Thread t_hanlder = new Thread(doChat); // 클라이언트 들어오면 쓰레드 돌림
                t_hanlder.IsBackground = true;
                t_hanlder.Start();
            }

            public delegate void ClientRequestHandler(int requestType, byte[] readBuffer, int cli_num);
            public event ClientRequestHandler OnRequest;

            public delegate void DisconnectedHandler(TcpClient clientSocket);
            public event DisconnectedHandler OnDisconnected;

            private void doChat()
            {
                NetworkStream stream = null;
                try
                {
                    byte[] readbuffer = new byte[1024 * 4];
                    int bytes = 0;
                    int MessageCount = 0;
                    int cli_num = 0;

                    while (true) // 클라이언트로부터 메시지 받는 것을 계속 대기중
                    {
                        MessageCount++;
                        try
                        {
                            stream = clientSocket.GetStream(); // 클라이언트 소켓 받고 읽음
                            bytes = stream.Read(this.readbuffer, 0, this.readbuffer.Length); 
                            Packet packet = (Packet)Packet.Deserialize(this.readbuffer);
                            cli_num = clientNum[clientSocket];
                            if (OnRequest != null)
                                OnRequest(packet.Type, this.readbuffer, cli_num);
                        }
                        catch (Exception e)
                        {
                            MessageBox.Show(e.Message + e.StackTrace);
                        }
                    }
                }
                catch (SocketException se)
                {
                    Trace.WriteLine(string.Format("doChat - SocketException : {0}", se.Message));

                    if (clientSocket != null)
                    {
                        if (OnDisconnected != null)
                            OnDisconnected(clientSocket);

                        clientSocket.Close();
                        stream.Close();
                    }
                }
                catch (Exception ex)
                {
                    Trace.WriteLine(string.Format("doChat - Exception : {0}", ex.Message));

                    if (clientSocket != null)
                    {
                        if (OnDisconnected != null)
                            OnDisconnected(clientSocket);

                        clientSocket.Close();
                        stream.Close();
                    }
                }
            }
        }

        private void richTextBox1_TextChanged(object sender, EventArgs e)
        {

        }
    }
}

