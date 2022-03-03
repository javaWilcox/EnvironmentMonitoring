using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Data.SqlClient;
using System.Windows.Forms.DataVisualization.Charting;

namespace 传感器监测
{
    public partial class formMain : Form
    {
        private Socket localSocket;
        private List<Socket> clientSockets;

        private Thread tcpServerAcceptThread;
        private List<Thread> clientRecvThreads;

        private Thread udpClientRecvThread;

        private byte[] FrameBuffer;
        private int FrameBufferCount = 0;
        private readonly int FrameBufferSize = 14;

        private SqlConnection sqlConnection;

        public formMain()
        {
            InitializeComponent();

            //获取本机所有IP
            String hostName = Dns.GetHostName();
            IPHostEntry iPHostEntry = Dns.GetHostEntry(hostName);
            foreach (IPAddress ipAddress in iPHostEntry.AddressList)
            {
                if (ipAddress.AddressFamily == AddressFamily.InterNetwork)
                {
                    cbLocalIP.Items.Add(ipAddress.ToString());
                }
            }
            cbLocalIP.SelectedIndex = 0;

            InitChart(chartTDS1, "水质", Color.Blue, 0, 1000);
            InitChart(chartTemp1, "温度", Color.Red, 0, 35);
            InitChart(chartHum1, "湿度", Color.Green, 0, 100);
            InitChart(chartLight1, "光强", Color.Tomato, 0, 1000);

            InitChart(chartTurb2, "浊度", Color.Blue, 0, 10);
            InitChart(chartTemp2, "温度", Color.Red, 0, 35);
            InitChart(chartHum2, "湿度", Color.Green, 0, 100);
            InitChart(chartLight2, "光强", Color.Tomato, 0, 1000);
        }

        private void btnStart_Click(object sender, EventArgs e)
        {
            clientSockets = new List<Socket>();
            clientRecvThreads = new List<Thread>();
            FrameBuffer = new byte[FrameBufferSize];
            String localIP = cbLocalIP.SelectedItem.ToString();
            int localPort = Convert.ToInt32(tbLocalPort.Text);

            if (rdbTCP.Checked)
            {
                StartTcpServer(localIP, localPort);
            }
            else if (rdbUDP.Checked)
            {
                StartUdp(localIP, localPort);
            }

            //连接SQL服务器
            String sqlStr = "server='.\\qjy';" +
                "database='shixun3';" +
                "uid='sa';" +
                "pwd='123456';";
            sqlConnection = new SqlConnection(sqlStr);
            sqlConnection.Open();
            ClearRecord();
            DispDatabase();

            cbLocalIP.Enabled = false;
            tbLocalPort.Enabled = false;
            btnStart.Enabled = false;
            btnStop.Enabled = true;
            pnProtocol.Enabled = false;
        }

        private void btnStop_Click(object sender, EventArgs e)
        {
            if (rdbTCP.Checked)
            {
                /*
                //关闭线程
                tcpServerAcceptThread.Abort();
                foreach (Thread thread in clientRecvThreads)
                {
                    thread.Abort();
                }
                */
                //关闭套接字
                foreach (Socket socket in clientSockets)
                {
                    socket.Close();
                }
                localSocket.Close();

                DispInfo("TCP服务器已关闭！");
            }
            else if (rdbUDP.Checked)
            {
                localSocket.Close();
                DispInfo("UDP连接已断开！");
            }

            sqlConnection.Close();
            sqlConnection.Dispose();

            cbLocalIP.Enabled = true;
            tbLocalPort.Enabled = true;
            btnStart.Enabled = true;
            btnStop.Enabled = false;
            pnProtocol.Enabled = true;
        }

        private void StartTcpServer(String ip, int port)
        {
            IPAddress address = IPAddress.Parse(ip);
            IPEndPoint ipep = new IPEndPoint(address, port);
            localSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            localSocket.Bind(ipep);
            localSocket.Listen(10);
            DispInfo("TCP服务器建立成功！");
            tcpServerAcceptThread = new Thread(new ThreadStart(TcpServerAcceptThread));
            tcpServerAcceptThread.IsBackground = true;
            tcpServerAcceptThread.Start();
        }

        private void TcpServerAcceptThread()
        {
            while (true)
            {
                try
                {
                    Socket socket = localSocket.Accept();
                    clientSockets.Add(socket);
                    String remoteName = socket.RemoteEndPoint.ToString().Trim();
                    AddClientIp(remoteName);
                    DispInfo("客户端" + socket.RemoteEndPoint.ToString().Trim() + "已连接！");

                    Thread thread = new Thread(TcpClientReceiveThread);
                    thread.IsBackground = true;
                    thread.Start(socket);
                    clientRecvThreads.Add(thread);
                }
                catch (Exception ex)
                {
                    RemoveClientIp("all");
                    Console.WriteLine(ex.Message);
                    return;
                }
            }
        }

        private void TcpClientReceiveThread(Object socket)
        {
            Socket clientSocket = (Socket)socket;
            String clientInfo = clientSocket.RemoteEndPoint.ToString().Trim();
            byte[] buffer = new byte[1024];
            int length = -1;
            while (true)
            {
                try
                {
                    length = clientSocket.Receive(buffer);
                    //解析数据
                    for (int i = 0; i < length; i++)
                    {
                        DataReceived(buffer[i]);
                    }
                }
                catch (Exception ex)
                {
                    RemoveClientIp(clientInfo);
                    Console.WriteLine(ex.Message);
                    return;
                }
            }
        }

        private void StartUdp(String ip, int port)
        {
            IPAddress address = IPAddress.Parse(ip);
            IPEndPoint ipep = new IPEndPoint(address, port);
            localSocket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            localSocket.Bind(ipep);
            DispInfo("UDP建立成功！");

            udpClientRecvThread = new Thread(new ThreadStart(udpClientReceiveThread));
            udpClientRecvThread.IsBackground = true;
            udpClientRecvThread.Start();
        }

        private void udpClientReceiveThread()
        {
            EndPoint ep = new IPEndPoint(IPAddress.Any, 0);
            byte[] buffer = new byte[1024];
            int length = -1;
            while (true)
            {
                try
                {
                    length = localSocket.ReceiveFrom(buffer, ref ep);
                    for (int i = 0; i < length; i++)
                    {
                        DataReceived(buffer[i]);
                    }

                    String clientInfo = ep.ToString();
                    Boolean found = false;
                    for (int i = 0; i < lbCilent.Items.Count; i++)
                    {
                        if (clientInfo.Equals(lbCilent.Items[i].ToString()))
                        {
                            found = true;
                            break;
                        }
                    }
                    if (!found)
                    {
                        AddClientIp(clientInfo);
                    }
                }
                catch (Exception ex)
                {
                    RemoveClientIp("all");
                    Console.WriteLine(ex.Message);
                    return;
                }
            }
        }

        private void DataReceived(byte b)
        {
            lock (this)
            {
                FrameBuffer[FrameBufferCount] = b;
                FrameBufferCount = (FrameBufferCount + 1) % FrameBufferSize;
                if (FrameBuffer[(FrameBufferCount + FrameBufferSize - 14) % FrameBufferSize] == 0xA5 &&
                    FrameBuffer[(FrameBufferCount + FrameBufferSize - 13) % FrameBufferSize] == 0xA5 &&
                    FrameBuffer[(FrameBufferCount + FrameBufferSize - 2) % FrameBufferSize] == 0x5A &&
                    FrameBuffer[(FrameBufferCount + FrameBufferSize - 1) % FrameBufferSize] == 0x5A)
                {
                    byte check = 0;
                    for (int i = 4; i <= 12; i++)
                    {
                        check ^= FrameBuffer[(FrameBufferCount + FrameBufferSize - i) % FrameBufferSize];
                    }
                    byte correct = FrameBuffer[(FrameBufferCount + FrameBufferSize - 3) % FrameBufferSize];
                    if (check != correct)
                    {
                        DispInfo("校验错误！" + check + "-" + correct);
                        //return;
                    }

                    int id = FrameBuffer[(FrameBufferCount + FrameBufferSize - 12) % FrameBufferSize];

                    int tdsOrTurb = FrameBuffer[(FrameBufferCount + FrameBufferSize - 11) % FrameBufferSize];
                    tdsOrTurb <<= 8;
                    tdsOrTurb |= FrameBuffer[(FrameBufferCount + FrameBufferSize - 10) % FrameBufferSize];

                    int temp = FrameBuffer[(FrameBufferCount + FrameBufferSize - 9) % FrameBufferSize];
                    temp <<= 8;
                    temp |= FrameBuffer[(FrameBufferCount + FrameBufferSize - 8) % FrameBufferSize];

                    int hum = FrameBuffer[(FrameBufferCount + FrameBufferSize - 7) % FrameBufferSize];
                    hum <<= 8;
                    hum |= FrameBuffer[(FrameBufferCount + FrameBufferSize - 6) % FrameBufferSize];

                    int light = FrameBuffer[(FrameBufferCount + FrameBufferSize - 5) % FrameBufferSize];
                    light <<= 8;
                    light |= FrameBuffer[(FrameBufferCount + FrameBufferSize - 4) % FrameBufferSize];

                    //显示信息
                    DispSensorInfo(id, tdsOrTurb, temp, hum, light);
                    //画图
                    if (id == 1)
                    {
                        AddPoint(chartTDS1, tdsOrTurb);
                        AddPoint(chartTemp1, temp);
                        AddPoint(chartHum1, hum);
                        AddPoint(chartLight1, light);
                    }
                    else if (id == 2)
                    {
                        AddPoint(chartTurb2, tdsOrTurb / 100.0);
                        AddPoint(chartTemp2, temp);
                        AddPoint(chartHum2, hum);
                        AddPoint(chartLight2, light);
                    }
                    //添加到数据库
                    String devStr = "";
                    if (id == 1)
                    {
                        devStr = "WIFI";
                    }
                    else if (id == 2)
                    {
                        devStr = "蓝牙";
                    }
                    String tdsOrTurbStr = id == 1 ? tdsOrTurb + "ppm" : (tdsOrTurb / 100.0) + "%";
                    String tempStr = temp + "℃";
                    String humStr = hum + "%";
                    String lightStr = light + "Lux";
                    AddRecord(devStr, tdsOrTurbStr, tempStr, humStr, lightStr);
                    DispDatabase();
                }
            }
        }

        private void AddRecord(String device, String tdsOrTurb, String Temp, String Hum, String Light)
        {
            SqlCommand cmd = new SqlCommand();
            cmd.CommandText = "insert into SensorInfo values (" +
                "\'" + device + "\'," +
                "\'" + tdsOrTurb + "\'," +
                "\'" + Temp + "\'," +
                "\'" + Hum + "\'," +
                "\'" + Light + "\'" +
                ")";
            cmd.Connection = sqlConnection;
            cmd.ExecuteNonQuery();
        }

        private void ClearRecord()
        {
            SqlCommand cmd = new SqlCommand();
            cmd.CommandText = "truncate table SensorInfo";
            cmd.Connection = sqlConnection;
            cmd.ExecuteNonQuery();
        }

        private void RrefreshDataView()
        {
            try
            {
                SqlDataAdapter adapter = new SqlDataAdapter();
                SqlCommand cmd = new SqlCommand();
                cmd.CommandText = "select " +
                    "device as 设备," +
                    "otherSensor as 水质或浊度," +
                    "temperature as 温度," +
                    "humidity as 湿度," +
                    "light as 光强" +
                    " from SensorInfo";
                cmd.Connection = sqlConnection;
                adapter.SelectCommand = cmd;
                DataSet set = new DataSet();
                adapter.Fill(set, "SensorInfo");
                dataView.DataSource = set.Tables["SensorInfo"];
                dataView.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.DisplayedCells;
                dataView.FirstDisplayedScrollingRowIndex = dataView.RowCount - 1;
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
            }
        }

        private void InitChart(Chart chart, String title, Color color, int minY, int maxY)
        {
            Series series = chart.Series[0];
            series.ChartType = SeriesChartType.FastLine;
            series.BorderWidth = 2;
            series.Color = color;

            chart.Legends[0].Enabled = false;

            chart.Titles.Clear();
            chart.Titles.Add(title);
            chart.Titles[0].Text = title;

            ChartArea chartArea = chart.ChartAreas[0];
            chartArea.AxisX.Minimum = 0;
            chartArea.AxisY.Minimum = minY;
            chartArea.AxisY.Maximum = maxY;

            chartArea.AxisX.ScrollBar.IsPositionedInside = false;
            chartArea.AxisX.ScrollBar.Enabled = true;
            chartArea.AxisX.ScaleView.Position = 0;
            chartArea.AxisX.LabelStyle.ForeColor = Color.White;
            chartArea.AxisX.LabelAutoFitStyle = LabelAutoFitStyles.None;
        }

        private delegate void DispLineChartDelegate(Chart chart, double value);
        private void AddPoint(Chart chart, double value)
        {
            if (chart.InvokeRequired)
            {
                DispLineChartDelegate dispLineChartDelegate = new DispLineChartDelegate(AddPoint);
                chart.Invoke(dispLineChartDelegate, new Object[] { chart, value });
            }
            else
            {
                Series series = chart.Series[0];
                series.Points.AddXY(series.Points.Count, value);

                ChartArea chartArea = chart.ChartAreas[0];
                chartArea.AxisX.ScaleView.Position = series.Points.Count - 30;
                chartArea.AxisX.ScaleView.Size = 30;
            }
        }

        private delegate void DispDelegate(String str);
        private void DispInfo(String str)
        {
            tsInfo.Text = str;
        }
        private void AddClientIp(String str)
        {
            if (lbCilent.InvokeRequired)
            {
                DispDelegate dispDelegate = new DispDelegate(AddClientIp);
                lbCilent.Invoke(dispDelegate, new String[] { str });
            }
            else
            {
                lbCilent.Items.Add(str);
            }
        }
        private void RemoveClientIp(String str)
        {
            if (lbCilent.InvokeRequired)
            {
                DispDelegate dispDelegate = new DispDelegate(RemoveClientIp);
                lbCilent.Invoke(dispDelegate, new String[] { str });
            }
            else
            {
                if (str.Equals("all"))
                {
                    lbCilent.Items.Clear();
                }
                else
                {
                    lbCilent.Items.Remove(str);
                }
            }
        }

        private delegate void DispSensorInfoDelegate(int id, int tdsOrTurb, int temp, int hum, int light);
        private void DispSensorInfo(int id, int tdsOrTurb, int temp, int hum, int light)
        {
            if (tbTDS1.InvokeRequired)
            {
                DispSensorInfoDelegate dispSensorInfoDelegate = new DispSensorInfoDelegate(DispSensorInfo);
                lbCilent.Invoke(dispSensorInfoDelegate, new object[] { id, tdsOrTurb, temp, hum, light });
            }
            else
            {
                if (id == 1)
                {
                    tbTDS1.Text = tdsOrTurb + "";
                    tbTemp1.Text = temp + "";
                    tbHum1.Text = hum + "";
                    tbLight1.Text = light + "";
                }
                else if (id == 2)
                {
                    tbTurb2.Text = tdsOrTurb / 100.0f + "";
                    tbTemp2.Text = temp + "";
                    tbHum2.Text = hum + "";
                    tbLight2.Text = light + "";
                }
            }
        }

        private delegate void DispDatabaseDelegate();
        private void DispDatabase()
        {
            if (dataView.InvokeRequired)
            {
                DispDatabaseDelegate dispDatabaseDelegate = new DispDatabaseDelegate(DispDatabase);
                dataView.Invoke(dispDatabaseDelegate, null);
            }
            else
            {
                RrefreshDataView();
            }
        }
    }
}
