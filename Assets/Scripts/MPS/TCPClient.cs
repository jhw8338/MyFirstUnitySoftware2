using System.Net.Sockets;
using System;
using UnityEngine;
using System.IO;
using System.Text;
using TMPro;
using System.Collections;
using UnityEngine.Windows;
using System.Threading.Tasks;
using System.Threading;
using System.Collections.Generic;
using System.Linq;

public class TCPClient : MonoBehaviour
{
    public static TCPClient Instance;

    [SerializeField] TMP_InputField dataInput;
    [SerializeField] string dataToServer;
    [SerializeField] string dataFromServer;
    public bool isConnected;
    public float interval;
    public int xDeviceBlockSize;
    public int yDeviceBlockSize;
    public int dDeviceBlockSize;
    public string xDevices;
    public string yDevices;
    public string dDevices;

    TcpClient client;
    NetworkStream stream;
    string msg;
    string totalMsg;

    private void Awake()
    {
        if(Instance == null)
            Instance = this;
    }

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        try
        {
            // 1. 서버에 연결
            client = new TcpClient("127.0.0.1", 12345);
            print("서버에 연결됨");

            // 2. 네트워크 스트림 얻기
            stream = client.GetStream();
        }
        catch(Exception ex)
        {
            print(ex);
            print("서버를 먼저 작동시켜 주세요.");
        }


        for (int i = 0; i < xDeviceBlockSize; i++)
        {
            xDevices += "0000000000000000";
        }

        for (int i = 0; i < yDeviceBlockSize; i++)
        {
            yDevices += "0000000000000000";
        }

        for (int i = 0; i < dDeviceBlockSize; i++)
        {
            dDevices += "0000000000000000";
        }
    }

    CancellationTokenSource cts;
    Task t;
    public void OnConnectBtnClkEvent()
    {
        if(!isConnected)
        {
            Request("Connect"); // CONNECTED 일 경우 OK

            StartCoroutine(CoRequest());
        }
    }

    public void OnDisconnectBtnClkEvent()
    {
        msg = Request("Disconnect"); // DISCONNECTED 일 경우 OK

        if(isConnected)
        {
            isConnected = false;

            //if (!isConnected)
            //{
            //    if(t != null && cts != null)
            //        cts.Cancel();
            //}
        }
    }

    IEnumerator CoRequest()
    {
        yield return new WaitUntil(() => isConnected);

        print("CoRequest");

        if (isConnected)
        {
            cts = new CancellationTokenSource();
            CancellationToken token = cts.Token;
            t = Task.Run(() => RequestAsync(), token);
        }

        while (isConnected)
        {
            try
            {
                //string data = Request("temp"); // zGET,X0,1 / SET,X0,128


                // SET,X0,128,GET,Y0,2,GET,D0,3 -> 서버로 전송 -> WriteDeviceBlock 1번, ReadDeviceBlock 1번

                // 1. MPS의 X 디바이스 정보를 정수형으로 전달한다.
                string returnValue = WriteDevices("X0", 2, xDevices); // SET,X0,128
                print("ScanPLC: " + dataFromServer); // PLC신호(실린더 신호(32) + 램프신호(22)): "32,22"

                // 2. PLC의 Y, D 디바이스 정보를 2진수 형태로 받는다.
                yDevices = ReadDevices("Y0", 2); //  GET,Y0,2
                                                 //dDevices = ReadDevices("D0", 1); //  GET,D0,1
            }
            // 3. 통합: 서버에서 데이터를 주고 받은 후 원하는 데이터만 받기
            // (Unity to Server 데이터 형식: SET,X0,3,128,24,1/GET,Y0,2/GET,D0,3)
            // (Server to Unity 데이터 형식: X0,123,24/D0,23
            //Request($"SET,X0,1,{xDevices}/GET,Y0,2/GET,D0,1");
            catch (Exception ex)
            {
                print(ex);
            }

            yield return new WaitForSeconds(interval);
        }
    }

    public string WriteDevices(string deviceName, int blockSize, string data)
    {
        totalMsg = "";

        // data = "1101010001000000" or "110101000100000011010100010000001101010001000000" -> 555
        int[] convertedData = new int[blockSize];

        ConvertData(data);

        void ConvertData(string data)
        {
            for (int i = 0; i < blockSize; i++)
            {
                string splited = data.Substring(i * 16, 16);        // 1101010001000000
                splited = Reverse(splited);                         // 0000001000101011(reversed)
                convertedData[i] = Convert.ToInt32(splited, 2);     // 555(10진수 변환)
            }
        }

        // 128,64,266
        foreach(var d in convertedData)
        {
            totalMsg += "," + d;
        }

        // Server로 데이터 전송
        dataToServer = $"GET,Y0,2,SET,{deviceName},{blockSize}{totalMsg}";
        //print($"SET,{deviceName},{blockSize}{totalMsg}");
        //string returnValue = Request($"SET,{deviceName},{blockSize}{totalMsg}"); // SET,X0,3,128,64,266
        return dataToServer;
    }

    public string ReadDevices(string deviceName, int blockSize)
    {
        string returnValue = dataFromServer; // "Y0,0,0"

        int[] data = new int[blockSize];
        string totalData = "";

        if (returnValue != "에러코드")
        {
            print("디바이스 블록 읽기가 완료되었습니다.");

            data = returnValue.Split(',').Select(n => Convert.ToInt32(n)).ToArray();

            foreach (int d in data)
            {
                string input = Convert.ToString(d, 2); // D 디바이스: 10진수, 데이터 처리를 위해 사용
                                                       // (생산된 물건의 개수, 생산해야 할 물건의 남은 개수, 불량의 개수)
                                                       // 데이터 예시: 55, 100
                                                       // X, Y 디바이스: 2진수, 비트 단위로 사용
                                                       // 비트 단위 데이터 예시: 1011010, xDevice[0] = 1

                if (!deviceName.Contains("D")) // deviceName = "Y0", "X0"
                {
                    input = Reverse(input);

                    // x[33] = 0 -> x[3][3]
                    if (16 - input.Length > 0) // 1101010001 -> 110101000100000 
                    {
                        int countZero = 16 - input.Length;
                        for (int i = 0; i < countZero; i++)
                        {
                            input += '0';
                        }
                    }
                }

                totalData += input;
            }

            return totalData; // 110101000100000 / 110101000100000
        }
        else
        {
            return returnValue;
        }
    }

    public static string Reverse(string input)
    {
        char[] chars = input.ToCharArray();
        Array.Reverse(chars);
        return new string(chars);
    }

    public void Request()
    {
        byte[] dataBytes = Encoding.UTF8.GetBytes(dataInput.text);
        stream.Write(dataBytes, 0, dataBytes.Length);

        // 서버로부터 데이터 읽기
        byte[] buffer = new byte[1024];
        int bytesRead = stream.Read(buffer, 0, buffer.Length);
        string response = Encoding.UTF8.GetString(buffer, 0, bytesRead);
        Console.WriteLine("서버: " + response);
    }

    public string Request(string message)
    {
        byte[] dataBytes = Encoding.UTF8.GetBytes(message);
        stream.Write(dataBytes, 0, dataBytes.Length); // "Connect"

        // 서버로부터 데이터 읽기
        byte[] buffer = new byte[1024];
        int bytesRead = stream.Read(buffer, 0, buffer.Length);
        string response = Encoding.UTF8.GetString(buffer, 0, bytesRead);
        print("서버: " + response);

        if (response.Contains("CONNECTED"))
            isConnected = true;

        return response;
    }

    private async Task RequestAsync()
    {
        while (isConnected)
        {
            try
            {
                //WriteDevices("X0", 1, xDevices);

                // GET,Y0,2,SET,X0,2,32,10
                byte[] dataBytes = Encoding.UTF8.GetBytes(dataToServer);

                // NetworkStream에 데이터 쓰기
                await stream.WriteAsync(dataBytes, 0, dataBytes.Length);

                // 데이터 수신
                byte[] buffer = new byte[1024];
                int nBytes = await stream.ReadAsync(buffer, 0, buffer.Length);
                dataFromServer = Encoding.UTF8.GetString(buffer, 0, nBytes);
                print("dataFromServer: " + dataFromServer); // Y0,0,0
            }
            catch (Exception e)
            {
                print(e.ToString());
            }
        }
    }


    private void OnDestroy()
    {
        
        if (isConnected)
        {
        Request("Disconnect&Quit");
            isConnected = false;
        }
    }
}
