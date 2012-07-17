﻿/* --------------------------------------------------------------------------
 * Copyrights
 *
 * Portions created by or assigned to Sébastien Gissinger
 *
 * License
 *
 * Jabber-Net is licensed under the LGPL.
 * See LICENSE.txt for details.
 * --------------------------------------------------------------------------*/
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using Jabber.Stun.Attributes;

namespace Jabber.Stun
{
    /// <summary>
    /// Represents a STUN Client that can send request to a STUN Server
    /// and receive response from it (or not)
    /// </summary>
    public class StunClient
    {
        #region CONSTANTS
        /// <summary>
        /// IANA has assigned port number 3478 for the "stun"
        /// service, defined over TCP and UDP.
        /// </summary>
        public const Int32 DEFAULT_STUN_PORT = 3478;
        /// <summary>
        /// IANA has assigned port number 5349 for the "stuns" (stun-secured)
        /// service, defined over TCP and UDP.
        /// The UDP port is not currently defined. However, it is reserved for future use
        /// </summary>
        public const Int32 DEFAULT_STUNS_PORT = 5349;
        /// <summary>
        /// Socket receive buffer
        /// </summary>
        private const Int32 BUFFER = 8192;
        #endregion

        #region PROPERTIES
        /// <summary>
        /// Contains the socket instance used to communicate with the STUN Server
        /// </summary>
        public Socket Socket { get; private set; }
        /// <summary>
        /// Contains the type of protocol (udp, tcp) used to communicate with the STUN Server
        /// </summary>
        public ProtocolType ProtocolType { get; private set; }
        /// <summary>
        /// Contains the type of socket (datagram, stream) used to communicate with the STUN Server
        /// </summary>
        public SocketType SocketType { get; private set; }
        /// <summary>
        /// Contains the IPEndPoint of the STUN Server
        /// </summary>
        public IPEndPoint ServerEP { get; private set; }
        /// <summary>
        /// Contains the IPEndPoint of the LocalEndPoint used by this client
        /// Unless Reset() method is fired every new connection to a STUN Server will be bound to this IPEndPoint
        /// </summary>
        public IPEndPoint StunningEP { get; private set; }
        /// <summary>
        /// Contains True if the current connected Socket must use TLS over TCP to communicate with the STUN Server
        /// </summary>
        public Boolean UseSsl { get; private set; }
        /// <summary>
        /// Contains the TcpClient handling TLS over TCP connection with the STUN Server
        /// </summary>
        private TcpClient SslClient { get; set; }
        /// <summary>
        /// Contains the SslStream handling TLS over TCP communication with the STUN Server
        /// </summary>
        private SslStream SslStream { get; set; }
        #endregion


        #region CONSTRUCTORS & FINALIZERS
        /// <summary>
        /// Constructs a StunClient 
        /// </summary>
        public StunClient()
            : this(null)
        { }

        /// <summary>
        /// Constructs a StunClient using an existing IPEndPoint
        /// </summary>
        /// <param name="stunningEP">The IPEndPoint to which the socket will be bound to</param>
        public StunClient(IPEndPoint stunningEP)
        {
            this.StunningEP = stunningEP;
        }

        /// <summary>
        /// Sample usage of this StunClient
        /// </summary>
        /// <param name="args">Unused</param>
        private static void Main(string[] args)
        {
            KeyValuePair<IPEndPoint, MappedAddress> stunKeyValue = StunUtilities.GetMappedAddressFrom("66.228.45.110", ProtocolType.Udp);

            StunMessage msg = new StunMessage(StunMethodType.Binding, StunMethodClass.Request, StunUtilities.NewTransactionId);
            StunAttribute attr1 = new StunAttribute(StunAttributeType.Realm, "Hello World !");
            StunAttribute attr2 = new StunAttribute(StunAttributeType.Username, "Bob");

            msg.SetAttribute(attr1);
            msg.SetAttribute(attr2);

            byte[] octets = msg;
            StunMessage msgCopy = octets;

            // Reuse of an existing local IPEndPoint makes the three requests returning 
            // the same MappedAddress IPEndPoint if this client is behind a Cone NAT
            StunClient cli = new StunClient(stunKeyValue.Key);

            // Sample TLS over TCP working with ejabberd but may not work with the sample server IP given here
            cli.Connect("66.228.45.110", (sender, certificate, chain, sslPolicyErrors) => true);
            StunMessage op1 = cli.SendMessage(msgCopy);
            cli.Close();

            msgCopy.ClearAttributes();

            cli.Connect("132.177.123.13", ProtocolType.Udp);
            StunMessage op2 = cli.SendMessage(msgCopy);
            cli.Close();
        }
        #endregion

        #region METHODS
        /// <summary>
        /// Opens a socket connection to a STUN Server using UDP or TCP on default STUN port (3478)
        /// </summary>
        /// <param name="stunServerIp">The IP where the STUN Server can be reached</param>
        /// <param name="protocolType">The protocol type (UDP or TCP only) used to communicate with the STUN Server</param>
        public void Connect(String stunServerIp, ProtocolType protocolType)
        {
            this.Connect(stunServerIp, StunClient.DEFAULT_STUN_PORT, protocolType);
        }

        /// <summary>
        /// Opens a socket connection to a STUN Server using UDP or TCP on a given port
        /// </summary>
        /// <param name="stunServerIp">The IP where the STUN Server can be reached</param>
        /// <param name="stunServerPort">The port number on which the STUN Server is listening</param>
        /// <param name="protocolType">The protocol type (UDP or TCP only) used to communicate with the STUN Server</param>
        public void Connect(String stunServerIp, Int32 stunServerPort, ProtocolType protocolType)
        {
            this.Connect(new IPEndPoint(IPAddress.Parse(stunServerIp), stunServerPort), protocolType, false, null);
        }

        /// <summary>
        /// Opens a socket connection to a STUN Server using TLS over TCP on default STUNS port (5349)
        /// </summary>
        /// <param name="stunServerIp">The IP where the STUN Server can be reached</param>
        /// <param name="remoteCertificateValidationHandler">The callback handler which validate STUN Server TLS certificate</param>
        public void Connect(String stunServerIp, RemoteCertificateValidationCallback remoteCertificateValidationHandler)
        {
            this.Connect(stunServerIp, StunClient.DEFAULT_STUNS_PORT, remoteCertificateValidationHandler);
        }

        /// <summary>
        /// Opens a socket connection to a STUN Server using TLS over TCP on a given port
        /// </summary>
        /// <param name="stunServerIp">The IP where the STUN Server can be reached</param>
        /// <param name="stunServerPort">The port number on which the STUN Server is listening</param>
        /// <param name="remoteCertificateValidationHandler">The callback handler which validate STUN Server TLS certificate</param>
        public void Connect(String stunServerIp, Int32 stunServerPort, RemoteCertificateValidationCallback remoteCertificateValidationHandler)
        {
            this.Connect(new IPEndPoint(IPAddress.Parse(stunServerIp), stunServerPort), ProtocolType.Tcp, true, remoteCertificateValidationHandler);
        }

        /// <summary>
        /// Opens a socket connection to a STUN Server
        /// </summary>
        /// <param name="stunServerEP">The IPEndPoint where the STUN Server can be reached</param>
        /// <param name="protocolType">The protocol type (UDP or TCP only) used to communicate with the STUN Server</param>
        /// <param name="useSsl"></param>
        /// <param name="remoteCertificateValidationHandler"></param>
        private void Connect(IPEndPoint stunServerEP, ProtocolType protocolType, Boolean useSsl, RemoteCertificateValidationCallback remoteCertificateValidationHandler)
        {
            this.ServerEP = stunServerEP;
            this.ProtocolType = protocolType;
            this.UseSsl = useSsl;

            if (this.ProtocolType != ProtocolType.Tcp && this.ProtocolType != ProtocolType.Udp)
                throw new ArgumentException("Only UDP and TCP are acceptable values", "protocolType");

            if (this.UseSsl && this.ProtocolType != ProtocolType.Tcp)
                throw new ArgumentException("Only TCP can be used in conjunction with SSL", "useSsl");

            if (this.Socket != null)
                throw new ArgumentException("StunClient socket is not null, you must close it before doing a new connection", "this.Socket");


            if (this.ProtocolType == ProtocolType.Tcp)
                this.SocketType = SocketType.Stream;
            else
                this.SocketType = SocketType.Dgram;


            this.Socket = new Socket(this.ServerEP.AddressFamily, this.SocketType, this.ProtocolType);
            this.Socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, 1);


            if (this.StunningEP != null)
                this.Socket.Bind(this.StunningEP);


            if (this.UseSsl)
            {
                if (remoteCertificateValidationHandler == null)
                    throw new ArgumentException("You must provide a valid RemoteCertificateValidationCallback", "remoteCertificateValidationHandler");

                this.SslClient = new TcpClient();
                this.SslClient.Client = this.Socket;
                this.SslClient.Connect(this.ServerEP);

                this.SslStream = new SslStream(this.SslClient.GetStream(), false, remoteCertificateValidationHandler);

                IAsyncResult ar = this.SslStream.BeginAuthenticateAsClient(this.ServerEP.Address.ToString(), null, SslProtocols.Tls, true, null, null);

                ar.AsyncWaitHandle.WaitOne();

                this.SslStream.EndAuthenticateAsClient(ar);
            }
            else
            {
                this.Socket.Connect(this.ServerEP);
            }

            if (this.StunningEP == null)
                this.StunningEP = (IPEndPoint)this.Socket.LocalEndPoint;
        }

        /// <summary>
        /// Closes the socket but do not reset the StunningEP to make the client able
        /// to send requests to a STUN Server using the same StunningEP
        /// </summary>
        public void Close()
        {
            if (this.UseSsl)
            {
                this.SslStream.Close();
                this.SslStream = null;

                this.SslClient.Close();
                this.SslClient = null;

                this.UseSsl = false;
            }
            else
            {
                this.Socket.Shutdown(SocketShutdown.Both);
            }

            this.Socket.Close();
            this.Socket = null;
        }

        /// <summary>
        /// Resets everything and make this client able to connect to a STUN Server using a new StunningEP
        /// </summary>
        public void Reset()
        {
            if (this.Socket != null)
                this.Close();

            this.ServerEP = null;
            this.ProtocolType = ProtocolType.Unknown;
            this.SocketType = SocketType.Unknown;
            this.StunningEP = null;
        }

        /// <summary>
        /// Sends a StunMessage to the connected STUN Server.
        /// If the StunMessage is an indication it returns null because the STUN Server
        /// should not respond to this class of message as described in [RFC5389]
        /// </summary>
        /// <param name="msg">The StunMessage to send to the connected STUN Server</param>
        /// <returns>The STUN Server response StunMessage or null if msg parameter is an indication StunMessage</returns>
        public StunMessage SendMessage(StunMessage msg)
        {
            return this.SendMessage(msg.GetBytes(), msg.MethodClass != StunMethodClass.Indication);
        }

        /// <summary>
        /// Sends a StunMessage to the connected STUN Server.
        /// </summary>
        /// <param name="msg">The StunMessage to send to the connected STUN Server</param>
        /// <param name="waitForResponse">True if a response is expected from the STUN Server</param>
        /// <returns>
        /// The STUN Server response StunMessage
        /// or null if waitForResponse parameter is false
        /// or null if the received transaction ID is not identical to the sent transaction ID
        /// </returns>
        public StunMessage SendMessage(byte[] msg, Boolean waitForResponse)
        {
            byte[] result = new byte[StunClient.BUFFER];

            if (this.UseSsl)
            {
                this.SslStream.Write(msg, 0, msg.Length);

                if (waitForResponse)
                    this.SslStream.Read(result, 0, StunClient.BUFFER);
            }
            else
            {
                this.Socket.Send(msg, 0, msg.Length, SocketFlags.None);

                if (waitForResponse)
                    this.Socket.Receive(result, 0, StunClient.BUFFER, SocketFlags.None);
            }


            if (StunUtilities.ByteArraysEquals(((StunMessage)result).TransactionID, ((StunMessage)msg).TransactionID))
                return result;
            else
                return null;
        }
        #endregion
    }
}