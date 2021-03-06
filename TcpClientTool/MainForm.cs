﻿using Common;
using System;
using System.IO;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace TcpClientTool
{
    [MainForm(Name = "TCP Client Tool")]
    public partial class MainForm : Form
    {
        TcpClient tcpClient;
        Stream stream;

        public MainForm()
        {
            InitializeComponent();
            sourceIPAddress.InterfaceDeleted += SourceIPAddress_InterfaceDeleted;
        }

        public MainForm(TcpClient tcpClient, Stream stream) : this()
        {
            this.tcpClient = tcpClient;
            this.stream = stream;
            if (stream == null)
            {
                this.stream = tcpClient.GetStream();
            }
        }

        public MainForm(TcpClient tcpClient) : this(tcpClient, null)
        {

        }

        private async void MainForm_Load(object sender, EventArgs e)
        {
            if (stream != null)
            {
                useSSL.Checked = stream is SslStream;
                EnableDisable(true);
                try
                {
                    CancellationTokenSource cts =
                        new CancellationTokenSource();
                    await ReadAsync(cts.Token).ConfigureAwait(true);
                    cts.Dispose();
                }
                catch(ObjectDisposedException)
                {
                    // happens when form is disposed
                }
            }
        }

        private void SourceIPAddress_InterfaceDeleted()
        {
            CloseTcpClient();
            sourceIPAddress.TextValue = string.Empty;
        }

        private async void SendButton_Click(object sender, EventArgs e)
        {
            if (tcpClient == null || !tcpClient.Connected)
            {
                await CreateTcpClient().ConfigureAwait(true);
                if (tcpClient == null || !tcpClient.Connected)
                {
                    return;
                }
                await SendAsync().ConfigureAwait(true);
                CancellationTokenSource cts =
                    new CancellationTokenSource();
                await ReadAsync(cts.Token)
                    .ConfigureAwait(true); // will block here till ReadAsync is done
                cts.Dispose();
            }
            else
            {
                await SendAsync().ConfigureAwait(true);
            }
        }

        private async Task SendAsync()
        {
            sendButton.Enabled = false;

            int tickcount = 0;
            byte[] data = input.BinaryValue;
            if (data.Length <= 0)
            {
                sendButton.Enabled = true;
                MessageBox.Show(this, "Nothing to send.", this.Text);
                return;
            }

            tickcount = Environment.TickCount;
            try
            {
                await stream.WriteAsync(data, 0, data.Length).ConfigureAwait(true);
            }
            catch(IOException ex)
            {
                sendButton.Enabled = true;
                MessageBox.Show(this, ex.Message, this.Text);
                CloseTcpClient();
                return;
            }
            tickcount = Environment.TickCount - tickcount;

            status.Text = String.Format("Sent {0} byte(s) in {1} milliseconds",
                data.Length, tickcount);
            sendButton.Enabled = true;
        }

        private void ShowReceivedData(byte[] data, int length)
        {
            outputText.AppendBinary(data, length);
            if (outputText.AppendBinaryChecked)
            {
                outputText.AppendText(Environment.NewLine);
                outputText.AppendText(Environment.NewLine);
            }
        }

        private async Task CreateTcpClient()
        {
            if (tcpClient != null && tcpClient.Connected)
            {
                return;
            }

            if (string.Empty.Equals(destinationIPAddress.Text)
                || string.Empty.Equals(destinationPort.Text))
            {
                MessageBox.Show(this, "Please specify the destination IP address and/or port.", this.Text);
                return;
            }

            int port = 0;
            IPAddress ipAddress = IPAddress.Any;
            IPAddress remoteIPAddress;
            if (IPAddress.TryParse(destinationIPAddress.Text, out remoteIPAddress)
                && remoteIPAddress.AddressFamily == AddressFamily.InterNetworkV6)
            {
                ipAddress = IPAddress.IPv6Any;
            }
            if (!string.Empty.Equals(sourceIPAddress.TextValue))
            {
                try
                {
                    ipAddress = IPAddress.Parse(sourceIPAddress.TextValue);
                }
                catch (FormatException ex)
                {
                    MessageBox.Show(this, ex.Message, this.Text);
                    return;
                }
            }
            if (!string.Empty.Equals(sourcePort.Text))
            {
                port = int.Parse(sourcePort.Text);
            }
            IPEndPoint localEndPoint = new IPEndPoint(ipAddress, port);
            tcpClient = new TcpClient(ipAddress.AddressFamily);
            if (reuseAddress.Checked)
            {
                tcpClient.Client.SetSocketOption(SocketOptionLevel.Socket,
                    SocketOptionName.ReuseAddress, true);
            }

            try
            {
                tcpClient.Client.Bind(localEndPoint);
            }
            catch (SocketException ex)
            {
                tcpClient.Dispose();
                tcpClient = null;
                MessageBox.Show(this, ex.Message, this.Text);
                return;
            }

            try
            {
                await tcpClient.ConnectAsync(destinationIPAddress.Text,
                    int.Parse(destinationPort.Text)).ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, this.Text);
                CloseTcpClient();
                return;
            }

            stream = tcpClient.GetStream();
            if (useSSL.Checked)
            {
                X509Certificate2Collection certs = new X509Certificate2Collection();
                try
                {
                    if (!string.IsNullOrWhiteSpace(pfxPath.Text) && File.Exists(pfxPath.Text))
                    {
                        X509Certificate2 cert = new X509Certificate2(pfxPath.Text, pfxPassphrase.Text);
                        certs.Add(cert);
                    }
                }
                catch (CryptographicException ex)
                {
                    MessageBox.Show(this, ex.Message, this.Text);
                    CloseTcpClient();
                    return;
                }
                SslStream ssls = new SslStream(stream, true,
                    (sender, certificate, chain, sslPolicyErrors) =>
                    {
                        return true;
                    });
                try
                {
                    ssls.AuthenticateAsClient(string.Empty, certs, SslProtocols.Tls12, false);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, ex.Message, this.Text);
                    CloseTcpClient();
                    return;
                }
                stream = ssls;
            }

            EnableDisable(true);
        }

        private void EnableDisable(bool connected)
        {
            IPEndPoint localEndPoint = connected ? 
                (IPEndPoint)tcpClient.Client.LocalEndPoint : null;
            sourceIPAddress.TextValue = connected ? 
                localEndPoint.Address.ToString() : sourceIPAddress.TextValue;
            sourcePort.Text = connected ? 
                localEndPoint.Port.ToString() : sourcePort.Text;
            sourceIPAddress.Enabled = !connected;
            sourcePort.Enabled = !connected;
            reuseAddress.Enabled = !connected;
            IPEndPoint remoteEndPoint = connected ? 
                (IPEndPoint)tcpClient.Client.RemoteEndPoint : null;
            destinationIPAddress.Text = connected ? 
                remoteEndPoint.Address.ToString() : destinationIPAddress.Text;
            destinationPort.Text = connected ? 
                remoteEndPoint.Port.ToString() : destinationPort.Text;
            destinationIPAddress.Enabled = !connected;
            destinationPort.Enabled = !connected;
            useSSL.Enabled = !connected;
            pfxPath.Enabled = !connected && useSSL.Checked;
            browseForPfx.Enabled = !connected && useSSL.Checked;
            pfxPassphrase.Enabled = !connected && useSSL.Checked;
            close.Enabled = connected;
            open.Enabled = !connected;
        }

        private void MainForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            CloseTcpClient();
        }

        private async Task ReadAsync(CancellationToken cancellationToken)
        {
            byte[] data = new byte[1024];

            while (true)
            {
                int length = 0;
                try
                {
                    if (stream == null)
                    {
                        return;
                    }
                    length = await 
                        stream.ReadAsync(data, 0, data.Length, cancellationToken)
                        .ConfigureAwait(true);
                }
                catch (IOException ex)
                {
                    MessageBox.Show(this, ex.Message, this.Text);
                    CloseTcpClient();
                    return;
                }
                catch(ObjectDisposedException)
                {
                    CloseTcpClient();
                    return;
                }
                if (length == 0)
                {
                    break;
                }
                ShowReceivedData(data, length);
            }
        }

        private void CloseTcpClient()
        {
            if (tcpClient != null)
            {
                if (tcpClient.Connected)
                {
                    tcpClient.Close();
                }
                tcpClient = null;
                stream = null;
            }
            EnableDisable(false);
        }

        private void Close_Click(object sender, EventArgs e)
        {
            CloseTcpClient();
        }

        private async void Open_Click(object sender, EventArgs e)
        {
            if (tcpClient != null && tcpClient.Connected)
            {
                return;
            }
            await CreateTcpClient().ConfigureAwait(true);
            if (tcpClient == null || !tcpClient.Connected)
            {
                return;
            }
            CancellationTokenSource cts =
                new CancellationTokenSource();
            await ReadAsync(cts.Token).ConfigureAwait(true);
            // will block here till ReadAsync is done
            cts.Dispose();
        }

        private void UseSSL_CheckedChanged(object sender, EventArgs e)
        {
            pfxPath.Enabled = useSSL.Checked;
            browseForPfx.Enabled = useSSL.Checked;
            pfxPassphrase.Enabled = useSSL.Checked;
        }

        private void BrowseForPfx_Click(object sender, EventArgs e)
        {
            DialogResult r = openFileDialog.ShowDialog();
            if (r == DialogResult.OK)
            {
                pfxPath.Text = openFileDialog.FileName;
            }
        }
    }
}
