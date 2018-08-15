﻿//-----------------------------------------------------------------------------
// FILE:	    HiveLogin.cs
// CONTRIBUTOR: Jeff Lill
// COPYRIGHT:	Copyright (c) 2016-2018 by neonFORGE, LLC.  All rights reserved.

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics.Contracts;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Serialization;

using Neon.Common;
using Neon.Cryptography;

namespace Neon.Hive
{
    /// <summary>
    /// <para>
    /// Holds the <b>sensitive</b> information required to remotely manage an operating
    /// neonHIVE using the <b>neon-cli</b>.
    /// </para>
    /// <note>
    /// <b>WARNING:</b> The information serialized by this class must be carefully protected
    /// because it can be used to assume control over a hive.
    /// </note>
    /// </summary>
    public class HiveLogin
    {
        /// <summary>
        /// Returns the hive name.
        /// </summary>
        [JsonIgnore]
        public string HiveName
        {
            get { return Definition?.Name; }
        }

        /// <summary>
        /// Returns the login name formatted as: USERNAME@HIVENAME
        /// </summary>
        [JsonIgnore]
        public string LoginName
        {
            get
            {
                if (Definition != null && Username != null)
                {
                    return $"{Username}@{HiveName}";
                }
                else
                {
                    return string.Empty;
                }
            }
        }

        /// <summary>
        /// The optional file system path where the hive login is persisted.  This is
        /// used by the <see cref="Save"/> method.
        /// </summary>
        [JsonIgnore]
        public string Path { get; set; }

        /// <summary>
        /// The operator's username associated with these hive secrets.
        /// </summary>
        [JsonProperty(PropertyName = "Username", Required = Required.Always)]
        public string Username { get; set; }

        /// <summary>
        /// Specifies whether communication with the hive should be made via
        /// the VPN or directly (the default).
        /// </summary>
        [JsonIgnore]
        public bool ViaVpn { get; set; }

        /// <summary>
        /// The hive definition.
        /// </summary>
        [JsonProperty(PropertyName = "Definition", Required = Required.Always)]
        public HiveDefinition Definition { get; set; }

        /// <summary>
        /// Indicates that the credentials are not fully initialized.  This will be <c>true</c> when
        /// a hive has been prepared but has not yet been fully setup.  This defaults to <c>true</c>
        /// and will be set to <c>false</c> after the hive has been fully configured.
        /// </summary>
        [JsonProperty(PropertyName = "SetupPending", Required = Required.Default, DefaultValueHandling = DefaultValueHandling.IgnoreAndPopulate)]
        [DefaultValue(true)]
        public bool SetupPending { get; set; } = true;

        /// <summary>
        /// Indicates that the login has hive root capabilities (e.g. managing the cloud infrastructure and other user logins).
        /// </summary>
        [JsonProperty(PropertyName = "IsRoot", Required = Required.Default, DefaultValueHandling = DefaultValueHandling.IgnoreAndPopulate)]
        [DefaultValue(false)]
        public bool IsRoot { get; set; }

        /// <summary>
        /// The root SSH username for the hive nodes.
        /// </summary>
        [JsonProperty(PropertyName = "SshUsername", Required = Required.Default, DefaultValueHandling = DefaultValueHandling.IgnoreAndPopulate)]
        [DefaultValue(null)]
        public string SshUsername { get; set; }

        /// <summary>
        /// The root SSH password password for the hive nodes.
        /// </summary>
        [JsonProperty(PropertyName = "SshPassword", Required = Required.Default, DefaultValueHandling = DefaultValueHandling.IgnoreAndPopulate)]
        [DefaultValue(null)]
        public string SshPassword { get; set; }

        /// <summary>
        /// Indicates whether a strong host SSH password was generated for the hive.
        /// This defaults to <c>false</c>.
        /// </summary>
        [JsonProperty(PropertyName = "HasStrongSshPassword", Required = Required.Default, DefaultValueHandling = DefaultValueHandling.IgnoreAndPopulate)]
        [DefaultValue(false)]
        public bool HasStrongSshPassword { get; set; }

        /// <summary>
        /// The public and private parts of the SSH client key when the hive is
        /// configured to authenticate clients via public keys or <c>null</c> when
        /// username/password authentication is enabled.
        /// </summary>
        [JsonProperty(PropertyName = "SshClientKey", Required = Required.Default, DefaultValueHandling = DefaultValueHandling.IgnoreAndPopulate)]
        [DefaultValue(null)]
        public SshClientKey SshClientKey { get; set; }

        /// <summary>
        /// The SSH RSA private key fingerprint used to secure the hive servers.  This is an 
        /// MD5 hash encoded as hex bytes separated by colons.
        /// </summary>
        [JsonProperty(PropertyName = "SshHiveHostKeyFingerprint")]
        public string SshHiveHostKeyFingerprint { get; set; }

        /// <summary>
        /// The SSH RSA private key used to secure the hive servers.
        /// </summary>
        [JsonProperty(PropertyName = "SshHiveHostPrivateKey")]
        public string SshHiveHostPrivateKey { get; set; }

        /// <summary>
        /// The SSH RSA private key used to secure the hive servers.
        /// </summary>
        [JsonProperty(PropertyName = "SshHiveHostPublicKey")]
        public string SshHiveHostPublicKey { get; set; }

        /// <summary>
        /// The HashiCorp Vault credentials.
        /// </summary>
        [JsonProperty(PropertyName = "VaultCredentials", Required = Required.Default, DefaultValueHandling = DefaultValueHandling.IgnoreAndPopulate)]
        [DefaultValue(null)]
        public VaultCredentials VaultCredentials { get; set;}

        /// <summary>
        /// The hive self-signed wildcard certificate covering <b>*.NAME.nhive.io</b>
        /// where <b>NAME</b> is the hive name.  This is used to secure HashCorp Vault
        /// as well as other hive endpoints that need to be secured such as dashboards.
        /// </summary>
        [JsonProperty(PropertyName = "HiveCertificate", Required = Required.Default)]
        [DefaultValue(null)]
        public TlsCertificate HiveCertificate { get; set; }

        /// <summary>
        /// The HashiCorp Vault self-signed wildcard certificate covering <b>*.neon-vault.NAME.nhive.io</b>
        /// where <b>NAME</b> is the hive name.
        /// </summary>
        [JsonProperty(PropertyName = "VaultCertificate", Required = Required.Default)]
        [DefaultValue(null)]
        public TlsCertificate VaultCertificate { get; set; }

        /// <summary>
        /// The Docker registry cache self-signed wildcard certificate covering <b>*.neon-registry-cache.NAME.nhive.io</b>
        /// where <b>NAME</b> is the hive name.
        /// </summary>
        [JsonProperty(PropertyName = "RegistryCacheCertificate", Required = Required.Default)]
        [DefaultValue(null)]
        public TlsCertificate RegistryCacheCertificate { get; set; }

        /// <summary>
        /// Returns the certificates that will need to be trusted so that
        /// [neon-cli] will function properly.
        /// </summary>
        [JsonIgnore]
        public IEnumerable<TlsCertificate> ClientCertificates
        {
            get
            {
                return new TlsCertificate[]
                {
                    HiveCertificate,
                    VaultCertificate
                };
            }
        }

        /// <summary>
        /// The Docker manager node swarm join key.
        /// </summary>
        [JsonProperty(PropertyName = "SwarmManagerToken", Required = Required.Default)]
        [DefaultValue(null)]
        public string SwarmManagerToken { get; set; }

        /// <summary>
        /// The Docker worker node swarm join key.
        /// </summary>
        [JsonProperty(PropertyName = "SwarmWorkerToken", Required = Required.Default)]
        [DefaultValue(null)]
        public string SwarmWorkerToken { get; set; }

        /// <summary>
        /// The VPN credentials if a management VPN is enabled for the hive.
        /// </summary>
        [JsonProperty(PropertyName = "VpnCredentials", Required = Required.Default, DefaultValueHandling = DefaultValueHandling.IgnoreAndPopulate)]
        [DefaultValue(null)]
        public VpnCredentials VpnCredentials { get; set; }

        /// <summary>
        /// Ceph Storage Cluster configuration.
        /// </summary>
        [JsonProperty(PropertyName = "Ceph", Required = Required.Default, DefaultValueHandling = DefaultValueHandling.IgnoreAndPopulate)]
        [DefaultValue(null)]
        public CephConfig Ceph { get; set; }

        /// <summary>
        /// Returns <c>true</c> if the login includes root HashiCorp Vault credentials.
        /// </summary>
        [JsonIgnore]
        public bool HasVaultRootCredentials
        {
            get { return VaultCredentials != null && !string.IsNullOrEmpty(VaultCredentials.RootToken); }
        }

        /// <summary>
        /// Used internally to indicate that any local machine initialization has already
        /// happened for this login.
        /// </summary>
        [JsonIgnore]
        internal bool InitMachine { get; set; }

        /// <summary>
        /// Returns the <see cref="SshCredentials"/> for the hive that can be used
        /// by <see cref="SshProxy{TMetadata}"/> and the <b>SSH.NET</b> Nuget package.
        /// </summary>
        /// <returns></returns>
        public SshCredentials GetSshCredentials()
        {
            if (SshClientKey != null)
            {
                return SshCredentials.FromPrivateKey(SshUsername, SshClientKey.PrivatePEM);
            }
            else if (!string.IsNullOrEmpty(SshUsername) && !string.IsNullOrEmpty(SshPassword))
            {
                return SshCredentials.FromUserPassword(SshUsername, SshPassword);
            }
            else
            {
                // $todo(jeff.lill):
                //
                // In the future, I expect that some hive services (like [neon-hive-manager])
                // may need to connect to cluster nodes.  For this to work, we'd need to have
                // some way to retrieve the SSH (and perhaps other credentials) from Vault
                // and set them somewhere in the [NeonHive] class (perhaps as the current
                // login).
                //
                // This note is repeated in: HiveProxy.cs

                return SshCredentials.None;
            }
        }

        /// <summary>
        /// Clears all root user secrets.
        /// </summary>
        public void ClearRootSecrets()
        {
            IsRoot                = false;
            SwarmManagerToken     = null;
            SwarmWorkerToken      = null;
            SshHiveHostPrivateKey = null;
            Ceph                  = null;

            if (VpnCredentials != null)
            {
                VpnCredentials.CaZipKey = null;
            }

            if (HiveCertificate != null)
            {
                HiveCertificate.KeyPem = null;
            }

            if (VaultCertificate != null)
            {
                VaultCertificate.KeyPem = null;
            }

            if (RegistryCacheCertificate != null)
            {
                RegistryCacheCertificate.KeyPem = null;
            }

            // Clear the Docker registry credentials.

            Definition.Docker.ClearSecrets();

            // Clear the provider specific information because it
            // contains hosting credentials.

            Definition.Hosting.ClearSecrets();
        }

        /// <summary>
        /// Returns a deep clone of the instance
        /// </summary>
        /// <returns>The clone.</returns>
        public HiveLogin Clone()
        {
            var json = NeonHelper.JsonSerialize(this);

            return NeonHelper.JsonDeserialize<HiveLogin>(json);
        }

        /// <summary>
        /// Persists the hive login information as JSON to the file system at <see cref="Path"/>.
        /// </summary>
        /// <exception cref="InvalidOperationException">Thrown is <see cref="Path"/> is not set.</exception>
        public void Save()
        {
            if (string.IsNullOrEmpty(Path))
            {
                throw new InvalidOperationException($"[{nameof(HiveLogin)}]  cannot be saved because [{nameof(Path)}] is null.");
            }

            File.WriteAllText(Path, NeonHelper.JsonSerialize(this, Formatting.Indented));
        }

        /// <inheritdoc/>
        public override string ToString()
        {
            return LoginName;
        }
    }
}
