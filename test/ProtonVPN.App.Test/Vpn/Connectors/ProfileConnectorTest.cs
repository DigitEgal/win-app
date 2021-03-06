﻿/*
 * Copyright (c) 2020 Proton Technologies AG
 *
 * This file is part of ProtonVPN.
 *
 * ProtonVPN is free software: you can redistribute it and/or modify
 * it under the terms of the GNU General Public License as published by
 * the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 *
 * ProtonVPN is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
 * GNU General Public License for more details.
 *
 * You should have received a copy of the GNU General Public License
 * along with ProtonVPN.  If not, see <https://www.gnu.org/licenses/>.
 */

using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NSubstitute;
using NSubstitute.Core;
using ProtonVPN.Common.Logging;
using ProtonVPN.Core.Api.Contracts;
using ProtonVPN.Core.Modals;
using ProtonVPN.Core.Models;
using ProtonVPN.Core.Profiles;
using ProtonVPN.Core.Servers;
using ProtonVPN.Core.Servers.Models;
using ProtonVPN.Core.Service.Vpn;
using ProtonVPN.Core.Settings;
using ProtonVPN.Vpn;
using ProtonVPN.Vpn.Connectors;
using Profile = ProtonVPN.Core.Profiles.Profile;

namespace ProtonVPN.App.Test.Vpn.Connectors
{
    [TestClass]
    public class ProfileConnectorTest
    {
        private readonly Common.Configuration.Config _config = new Common.Configuration.Config();

        private readonly ILogger _logger = Substitute.For<ILogger>();
        private readonly IUserStorage _userStorage = Substitute.For<IUserStorage>();
        private readonly IAppSettings _appSettings = Substitute.For<IAppSettings>();
        private readonly IVpnServiceManager _vpnServiceManager = Substitute.For<IVpnServiceManager>();
        private readonly IModals _modals = Substitute.For<IModals>();
        private readonly IDialogs _dialogs = Substitute.For<IDialogs>();

        private ServerManager _serverManager;
        private ServerCandidatesFactory _serverCandidatesFactory;
        private VpnCredentialProvider _vpnCredentialProvider;
        private ProfileConnector _profileConnector;

        private List<PhysicalServer> _standardPhysicalServers;
        private List<PhysicalServer> _p2pPhysicalServers;
        private List<PhysicalServer> _torPhysicalServers;
        private Server _standardServer;
        private Server _p2pServer;
        private Server _torServer;
        private List<Server> _servers;
        private ServerCandidates _candidates;
        private Profile _profile;

        [TestInitialize]
        public void Initialize()
        {
            InitializeDependencies();
            InitializeUser();
            InitializeArrangeVariables();
        }

        private void InitializeDependencies()
        {
            _serverManager = Substitute.For<ServerManager>(_userStorage);
            _serverCandidatesFactory = Substitute.For<ServerCandidatesFactory>(_serverManager, _userStorage);
            _vpnCredentialProvider = Substitute.For<VpnCredentialProvider>(_config, _appSettings, _userStorage);

            _profileConnector = new ProfileConnector(_logger, _userStorage, _appSettings,
                _serverManager, _serverCandidatesFactory, _vpnServiceManager, _modals, _dialogs, _vpnCredentialProvider);
        }

        private void InitializeUser()
        {
            User user = new User()
            {
                MaxTier = ServerTiers.Plus,
                VpnUsername = "Username1",
                VpnPassword = "Password1"
            };
            _userStorage.User().Returns(user);
        }

        private void InitializeArrangeVariables()
        {
            _standardPhysicalServers = new List<PhysicalServer> { new PhysicalServer(id: "Standard-PS", entryIp: "192.168.0.1", exitIp: "192.168.1.1", domain: "standard.protonvpn.ps", status: 1) };
            _p2pPhysicalServers = new List<PhysicalServer> { new PhysicalServer(id: "P2P-PS", entryIp: "192.168.0.2", exitIp: "192.168.1.2", domain: "p2p.protonvpn.ps", status: 1) };
            _torPhysicalServers = new List<PhysicalServer> { new PhysicalServer(id: "Tor-PS", entryIp: "192.168.0.3", exitIp: "192.168.1.3", domain: "tor.protonvpn.ps", status: 1) };

            _standardServer = new Server(id: "Standard-S", name: "Standard", city: "City", entryCountry: "CH", exitCountry: "CH", domain: "standard.protonvpn.s", status: 1, tier: ServerTiers.Basic,
                features: (sbyte)Features.None, load: 0, score: 1, location: Substitute.For<Location>(), physicalServers: _standardPhysicalServers, exitIp: "192.168.2.1");
            _p2pServer = new Server(id: "P2P-S", name: "P2P", city: "City", entryCountry: "CH", exitCountry: "CH", domain: "p2p.protonvpn.s", status: 1, tier: ServerTiers.Plus,
                features: (sbyte)Features.P2P, load: 100, score: 999, location: Substitute.For<Location>(), physicalServers: _p2pPhysicalServers, exitIp: "192.168.2.2");
            _torServer = new Server(id: "Tor-S", name: "Tor", city: "City", entryCountry: "CH", exitCountry: "CH", domain: "tor.protonvpn.s", status: 1, tier: ServerTiers.Plus,
                features: (sbyte)Features.Tor, load: 0, score: 0, location: Substitute.For<Location>(), physicalServers: _torPhysicalServers, exitIp: "192.168.2.3");
            _servers = new List<Server>
            {
                _standardServer,
                _p2pServer,
                _torServer
            };
            _candidates = new ServerCandidates(_serverManager, _userStorage, _servers);
            _profile = new Profile()
            {
                ProfileType = ProfileType.Fastest,
                Protocol = Protocol.Auto
            };
        }

        [TestMethod]
        public async Task Connect_PicksFastestServer()
        {
            // Arrange
            List<Server> expectedServers = new List<Server>
            {
                _torServer,
                _standardServer,
                _p2pServer
            };
            _vpnServiceManager.Connect(Arg.Any<VpnConnectionRequest>())
                .Returns((arg) => AssertConnectionAsync(arg, expectedServers));

            _appSettings.FeaturePortForwardingEnabled = true;
            _appSettings.PortForwardingEnabled = false;

            // Act
            await _profileConnector.Connect(_candidates, _profile);
        }

        private async Task AssertConnectionAsync(CallInfo callInfo, IList<Server> expectedServers)
        {
            VpnConnectionRequest vpnConnectionRequest = (VpnConnectionRequest)callInfo[0];
            Assert.AreEqual(expectedServers.Count, vpnConnectionRequest.Servers.Count);
            for (int i = 0; i < expectedServers.Count; i++)
            {
                Assert.AreEqual(expectedServers[i].Servers[0].EntryIp, vpnConnectionRequest.Servers[i].Ip);
            }
        }

        [TestMethod]
        public async Task Connect_PicksP2PServerWhenPortForwardingIsEnabled()
        {
            // Arrange
            List<Server> expectedServers = new List<Server>
            {
                _p2pServer,
                _torServer,
                _standardServer
            };
            _vpnServiceManager.Connect(Arg.Any<VpnConnectionRequest>())
                .Returns((arg) => AssertConnectionAsync(arg, expectedServers));

            _appSettings.FeaturePortForwardingEnabled = true;
            _appSettings.PortForwardingEnabled = true;

            // Act
            await _profileConnector.Connect(_candidates, _profile);
        }
    }
}
