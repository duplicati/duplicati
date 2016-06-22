//  Copyright (C) 2015, The Duplicati Team
//  http://www.duplicati.com, info@duplicati.com
//
//  This library is free software; you can redistribute it and/or modify
//  it under the terms of the GNU Lesser General Public License as
//  published by the Free Software Foundation; either version 2.1 of the
//  License, or (at your option) any later version.
//
//  This library is distributed in the hope that it will be useful, but
//  WITHOUT ANY WARRANTY; without even the implied warranty of
//  MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the GNU
//  Lesser General Public License for more details.
//
//  You should have received a copy of the GNU Lesser General Public
//  License along with this library; if not, write to the Free Software
//  Foundation, Inc., 59 Temple Place, Suite 330, Boston, MA 02111-1307 USA
using System;
using System.Collections.Generic;
using System.Linq;
using Duplicati.Library.Interface;
using Amazon.IdentityManagement;
using Amazon.IdentityManagement.Model;

namespace Duplicati.Library.Backend
{
	public class S3IAM : IWebModule
	{
		private const string KEY_OPERATION = "s3-operation";
		private const string KEY_USERNAME = "s3-username";
		private const string KEY_PASSWORD = "s3-password";
		private const string KEY_PATH = "s3-path";

		public enum Operation
		{
			IsRootAccount,
			CreateIAMUser,
			GetPolicyDoc
		}

		public const string POLICY_DOCUMENT_TEMPLATE = 
@"
{
    ""Version"": ""2012-10-17"",
    ""Statement"": [
        {
            ""Sid"": ""Stmt1390497858034"",
            ""Effect"": ""Allow"",
            ""Action"": [
                ""s3:GetObject"",
                ""s3:PutObject"",
                ""s3:ListBucket"",
                ""s3:DeleteObject""
            ],
            ""Resource"": [
                ""arn:aws:s3:::{0}"",
                ""arn:aws:s3:::{0}/*""
            ]
        }
    ]
}
";

		public S3IAM()
		{
		}

		public string Key { get { return "s3-iamconfig"; } }

		public string DisplayName { get { return "S3 IAM support module"; } }

		public string Description { get { return "Exposes S3 IAM manipulation as a web module"; } }


		public IList<ICommandLineArgument> SupportedCommands
		{
			get
			{
				return new List<ICommandLineArgument>(new ICommandLineArgument[] {
					new CommandLineArgument(KEY_OPERATION, CommandLineArgument.ArgumentType.Enumeration, "The operation to perform", "Selects the operation to perform", null, Enum.GetNames(typeof(Operation))),
					new CommandLineArgument(KEY_USERNAME, CommandLineArgument.ArgumentType.String, "The username", "The Amazon Access Key ID"),
					new CommandLineArgument(KEY_PASSWORD, CommandLineArgument.ArgumentType.String, "The password", "The Amazon Secret Key"),
				});
			}
		}

		public IDictionary<string, string> Execute(IDictionary<string, string> options)
		{
			string operationstring;
			string username;
			string password;
			string path;
			Operation operation;

			options.TryGetValue(KEY_OPERATION, out operationstring);
			options.TryGetValue(KEY_USERNAME, out username);
			options.TryGetValue(KEY_PASSWORD, out password);
			options.TryGetValue(KEY_PATH, out path);

			if (string.IsNullOrWhiteSpace(operationstring))
				throw new ArgumentNullException(KEY_OPERATION);
			if (string.IsNullOrWhiteSpace(username))
				throw new ArgumentNullException(KEY_USERNAME);
			if (string.IsNullOrWhiteSpace(password))
				throw new ArgumentNullException(KEY_PASSWORD);

			if (!Enum.TryParse(operationstring, true, out operation))
				throw new ArgumentException(string.Format("Unable to parse {0} as an operation", operationstring));

			switch (operation)
			{
				case Operation.GetPolicyDoc:
					return GetPolicyDoc(path);
				case Operation.CreateIAMUser:
					return CreateUnprivilegedUser(username, password, path);
				case Operation.IsRootAccount:
				default:
					return IsRoot(username, password);
			}
		}

		private Dictionary<string, string> GetPolicyDoc(string path)
		{
			var dict = new Dictionary<string, string>();
			dict["doc"] = GeneratePolicyDoc(path);
			return dict;
		}

		private string GeneratePolicyDoc(string path)
		{
			if (string.IsNullOrWhiteSpace(path))
				throw new ArgumentNullException("path");

			while (path.EndsWith("/", StringComparison.Ordinal))
				path = path.Substring(0, path.Length - 1);

			if (string.IsNullOrWhiteSpace(path))
				throw new ArgumentException("Invalid value for path");

			return string.Format(POLICY_DOCUMENT_TEMPLATE, path);
		}

		private Dictionary<string, string> IsRoot(string awsid, string awskey)
		{
			var cl = new AmazonIdentityManagementServiceClient(awsid, awskey);
			var user = cl.GetUser().User;

			var dict = new Dictionary<string, string>();
			dict["isroot"] = user.Arn.EndsWith(":root", StringComparison.Ordinal).ToString();
			dict["arn"] = user.Arn;
			dict["id"] = user.UserId;
			dict["name"] = user.UserName;
			dict["path"] = user.Path;

			return dict;
		}

		private Dictionary<string, string> CreateUnprivilegedUser(string awsid, string awskey, string path)
		{
			var now = Library.Utility.Utility.SerializeDateTime(DateTime.Now);
			var username = string.Format("duplicati-autocreated-backup-user-{0}", now);
			var policyname = string.Format("duplicati-autocreated-policy-{0}", now);
			var policydoc = GeneratePolicyDoc(path);

			var cl = new AmazonIdentityManagementServiceClient(awsid, awskey);
			var user = cl.CreateUser(new CreateUserRequest(username)).User;
			cl.PutUserPolicy(new PutUserPolicyRequest(
				user.UserName,
				policyname,
				string.Format(POLICY_DOCUMENT_TEMPLATE, path)
			));
			var key = cl.CreateAccessKey(new CreateAccessKeyRequest() { UserName = user.UserName }).AccessKey;

			var dict = new Dictionary<string, string>();
			dict["accessid"] = key.AccessKeyId;
			dict["secretkey"] = key.SecretAccessKey;

			return dict;
		}
	}
}

