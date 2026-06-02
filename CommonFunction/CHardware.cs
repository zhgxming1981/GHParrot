using System;
using System.IO;
using System.Linq;
using System.Management;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;

namespace CommonFunction
{
    public static class CHardware
    {

        /// <summary>
        /// 获取本机的Mac地址
        /// </summary>
        /// <returns></returns>
        public static string GetCurrentMacAddress()
        {
            try
            {
                var mac = NetworkInterface.GetAllNetworkInterfaces()
                    .Where(IsPhysicalNetworkInterface)
                    .OrderByDescending(GetNetworkInterfacePriority)
                    .ThenBy(x => x.Id)
                    .Select(x => FormatMacAddress(x.GetPhysicalAddress().GetAddressBytes()))
                    .FirstOrDefault(x => !string.IsNullOrEmpty(x));

                if (!string.IsNullOrEmpty(mac))
                {
                    return mac;
                }

                return GetMacAddressFromWmi();
            }
            catch
            {
                return string.Empty;
            }
        }

        private static bool IsPhysicalNetworkInterface(NetworkInterface networkInterface)
        {
            var physicalAddress = networkInterface.GetPhysicalAddress();
            if (physicalAddress == null || physicalAddress.GetAddressBytes().Length != 6)
                return false;

            if (networkInterface.NetworkInterfaceType == NetworkInterfaceType.Loopback ||
                networkInterface.NetworkInterfaceType == NetworkInterfaceType.Tunnel ||
                networkInterface.NetworkInterfaceType == NetworkInterfaceType.Unknown)
                return false;

            var name = (networkInterface.Name + " " + networkInterface.Description).ToLowerInvariant();
            return !name.Contains("virtual") &&
                   !name.Contains("vmware") &&
                   !name.Contains("hyper-v") &&
                   !name.Contains("virtualbox") &&
                   !name.Contains("vpn") &&
                   !name.Contains("bluetooth") &&
                   !name.Contains("pseudo") &&
                   !name.Contains("tap") &&
                   !name.Contains("loopback");
        }

        private static int GetNetworkInterfacePriority(NetworkInterface networkInterface)
        {
            var priority = 0;
            var ipProperties = networkInterface.GetIPProperties();

            if (networkInterface.NetworkInterfaceType == NetworkInterfaceType.Ethernet)
                priority += 100;
            else if (networkInterface.NetworkInterfaceType == NetworkInterfaceType.Wireless80211)
                priority += 90;

            if (ipProperties.GatewayAddresses.Any())
                priority += 20;

            if (ipProperties.DnsAddresses.Any())
                priority += 10;

            if (networkInterface.OperationalStatus == OperationalStatus.Up)
                priority += 1;

            return priority;
        }

        private static string FormatMacAddress(byte[] bytes)
        {
            if (bytes == null || bytes.Length != 6)
                return string.Empty;

            return string.Join("-", bytes.Select(x => x.ToString("X2")));
        }

        private static string GetMacAddressFromWmi()
        {
            using (var searcher = new ManagementObjectSearcher(
                "SELECT MACAddress FROM Win32_NetworkAdapter WHERE MACAddress IS NOT NULL AND PhysicalAdapter = TRUE"))
            using (var adapters = searcher.Get())
            {
                foreach (ManagementObject adapter in adapters)
                {
                    using (adapter)
                    {
                        var mac = adapter["MACAddress"] as string;
                        if (!string.IsNullOrEmpty(mac))
                            return mac.Replace(':', '-');
                    }
                }
            }

            return string.Empty;
        }


        private static bool? Legality = null;//是否合法，null表示未知




        private static string hostIP = null;
        public static string HostIP
        {
            get { return hostIP; }
        }


        [DllImport("ws2_32.dll")]
        private static extern int inet_addr(string cp);
        [DllImport("IPHLPAPI.dll")]
        private static extern int SendARP(Int32 DestIP, Int32 SrcIP, ref Int64 pMacAddr, ref Int32 PhyAddrLen);



        /// <summary>
        /// 通过局域网ip地址获取Mac地址
        /// </summary>
        /// <returns></returns>
        //private static string GetLanMacAddress(string ip)//获取远程IP（不能跨网段）的MAC地址
        //{
        //    string Mac = "";
        //    try
        //    {
        //        Int32 ldest = inet_addr(ip); //将IP地址从 点数格式转换成无符号长整型
        //        Int64 macinfo = new Int64();
        //        Int32 len = 6;
        //        SendARP(ldest, 0, ref macinfo, ref len);
        //        string TmpMac = Convert.ToString(macinfo, 16).PadLeft(12, '0');//转换成16进制　　注意有些没有十二位
        //        Mac = TmpMac.Substring(0, 2).ToUpper();
        //        for (int i = 2; i < TmpMac.Length; i = i + 2)
        //        {
        //            Mac = TmpMac.Substring(i, 2).ToUpper() + "-" + Mac;
        //        }
        //    }
        //    catch (Exception ex)
        //    {
        //        Mac = "获取远程主机的MAC错误：" + ex.Message;
        //    }
        //    return Mac;
        //}

        /// <summary>
        /// 校验合法性
        /// </summary>
        /// <returns></returns>
        public static bool CheckLegality()
        {
            if (Legality == null)
            {
                if (CheckCurrentMechine())
                {
                    hostIP = GetCurrentIP();
                    Legality = true;
                }
                //else if (CheckLanMechine())
                //{
                //    hostIP = lanIp;
                //    Legality = true;
                //}
                else
                {
                    Legality = false;
                }
            }
            return (bool)Legality;
        }


  





        public static bool CheckLegality2()
        {
            if (CheckCurrentMechine())
            {
                hostIP = GetCurrentIP();
                Legality = true;
            }
            //else if (CheckLanMechine())
            //{
            //    hostIP = lanIp;
            //    Legality = true;
            //}
            else
            {
                Legality = false;
            }
            return (bool)Legality;
        }

        /// <summary>
        /// 校核本机Mac是否合法
        /// </summary>
        /// < returns ></ returns >
        private static bool CheckCurrentMechine()
        {
            string mac = GetCurrentMacAddress();
            string c_fileName = "C:\\MyGHFile\\min_Parrot.txt";
            return CheckMechine(mac, c_fileName);
        }


        private static bool CheckMechine(string mac, string fileName)
        {
            string line = ReadFile(fileName);
            if (line == null)
                return false;
            string password = CreatePassword(mac);
            if (password == line)
                return true;
            else
                return false;
        }

        //private static bool CheckLanMechine()
        //{
        //    string localFileName = System.IO.Directory.GetCurrentDirectory() + "\\min_Parrot.txt";
        //    lanIp = ReadFile(localFileName);
        //    string mac = GetLanMacAddress(lanIp);//局域网mac

        //    string lanFileName = "\\\\" + lanIp + "\\MyGHFile\\min_Parrot.txt";
        //    return CheckMechine(mac, lanFileName);
        //}




        /// <summary>
        /// 根据Mac，生成password
        /// </summary>
        /// <param name="mac"></param>
        /// <returns></returns>
        private static string CreatePassword(string mac)
        {
            int len = mac.Length;
            char[] ch = new char[2 * len];
            for (int i = 0; i < len; i++)
            {
                byte[] array = System.Text.Encoding.ASCII.GetBytes(mac);
                int a = (int)array[i] + i % 2;
                ch[i] = (char)a;
                int b = 48 + (a + i) % 75;
                ch[i + len] = (char)b;
            }
            return String.Join("", ch.Reverse());
        }

        /// <summary>
        /// 读取指定的文件内容
        /// </summary>
        /// <param name="fullFileName"></param>
        /// <returns></returns>
        private static string ReadFile(string fullFileName)
        {
            if (File.Exists(fullFileName))
            {
                StreamReader sr = new StreamReader(fullFileName);
                string line = sr.ReadLine();
                sr.Close();
                sr.Dispose();
                return line;
            }
            return null;
        }


        public static string GetCurrentIP()
        {
            IPHostEntry host = Dns.GetHostEntry(Dns.GetHostName());
            foreach (IPAddress ip in host.AddressList)
            {
                if (ip.AddressFamily == AddressFamily.InterNetwork)
                {
                    return ip.ToString();
                }
            }
            return null;
        }
    }
}
