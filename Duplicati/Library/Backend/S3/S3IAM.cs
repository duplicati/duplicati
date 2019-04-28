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
            CanCreateUser,
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
                ""arn:aws:s3:::bucket-name"",
                ""arn:aws:s3:::bucket-name/*""
            ]
        }
    ]
}
";

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
            options.TryGetValue(KEY_OPERATION, out string operationstring);
            options.TryGetValue(KEY_USERNAME, out string username);
            options.TryGetValue(KEY_PASSWORD, out string password);
            options.TryGetValue(KEY_PATH, out string path);

            ValidateArgument(operationstring, KEY_OPERATION);

            if (!Enum.TryParse(operationstring, true, out Operation operation))
                throw new ArgumentException(string.Format("Unable to parse {0} as an operation", operationstring));

            switch (operation)
            {
                case Operation.GetPolicyDoc:
                    ValidateArgument(path, KEY_PATH);
                    return GetPolicyDoc(path);

                case Operation.CreateIAMUser:
                    ValidateArgument(username, KEY_USERNAME);
                    ValidateArgument(password, KEY_PASSWORD);
                    ValidateArgument(path, KEY_PATH);
                    return CreateUnprivilegedUser(username, password, path);

                default:
                    ValidateArgument(username, KEY_USERNAME);
                    ValidateArgument(password, KEY_PASSWORD);
                    return CanCreateUser(username, password);
            }
        }

        private static void ValidateArgument(string arg, string type)
        {
            if (string.IsNullOrWhiteSpace(arg))
            {
                throw new ArgumentNullException(type);
            }
        }

        private static IDictionary<string, string> GetPolicyDoc(string path)
        {
            return new Dictionary<string, string>
            {
                ["doc"] = GeneratePolicyDoc(path)
            };
        }

        private static string GeneratePolicyDoc(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentNullException(nameof(path));

            path = path.Trim().Trim('/').Trim();

            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException("Invalid value for path");

            var bucketname = path.Split('/').First();

            return POLICY_DOCUMENT_TEMPLATE.Replace("bucket-name", bucketname).Trim();
        }


        private static Boolean DetermineIfCreateUserIsAllowed(User user, AmazonIdentityManagementServiceClient cl)
        {
            var simulatePrincipalPolicy = new SimulatePrincipalPolicyRequest
            {
                PolicySourceArn = user.Arn,
                ActionNames = new[] { "iam:CreateUser" }.ToList()
            };

            return cl.SimulatePrincipalPolicy(simulatePrincipalPolicy).
                                        EvaluationResults.First().
                                        EvalDecision == PolicyEvaluationDecisionType.Allowed;

        }

        private static IDictionary<string, string> GetCreateUserDict(User user, AmazonIdentityManagementServiceClient cl)
        {
            var resultDict = new Dictionary<string, string>
            {
                ["isroot"] = false.ToString(),
                ["arn"] = user.Arn,
                ["id"] = user.UserId,
                ["name"] = user.UserName,
            };

            try
            {
                resultDict["isroot"] = DetermineIfCreateUserIsAllowed(user, cl).ToString();
            }
            catch (Exception ex)
            {
                return new Dictionary<string, string>
                {
                    // Can be removed if the UI code is updated to check for the "ex" key first
                    ["isroot"] = false.ToString(),
                    ["ex"] = ex.ToString(),
                    ["error"] = $"Exception occurred while testing if CreateUser is allowed for user {user.UserId} : {ex.Message}"
                };
            }

            return resultDict;
        }

        private static IDictionary<string, string> CanCreateUser(string awsid, string awskey)
        {
            var cl = new AmazonIdentityManagementServiceClient(awsid, awskey);
            User user;
            try
            {
                user = cl.GetUser().User;
            }
            catch (Exception ex) when (ex is NoSuchEntityException || ex is ServiceFailureException)
            {
                return new Dictionary<string, string>
                {
                    ["ex"] = ex.ToString(),
                    ["error"] = $"Exception occurred while retrieving user {awsid} : {ex.Message}"
                };
            }

            return GetCreateUserDict(user, cl);
        }

        private static IDictionary<string, string> CreateUnprivilegedUser(string awsid, string awskey, string path)
        {
            var now = Utility.Utility.SerializeDateTime(DateTime.Now);
            var username = string.Format("duplicati-autocreated-backup-user-{0}", now);
            var policyname = string.Format("duplicati-autocreated-policy-{0}", now);
            var policydoc = GeneratePolicyDoc(path);

            var cl = new AmazonIdentityManagementServiceClient(awsid, awskey);
            var user = cl.CreateUserAsync(new CreateUserRequest(username)).GetAwaiter().GetResult().User;
            cl.PutUserPolicyAsync(new PutUserPolicyRequest(
                user.UserName,
                policyname,
                policydoc
            ));
            var key = cl.CreateAccessKey(new CreateAccessKeyRequest { UserName = user.UserName }).AccessKey;

            return new Dictionary<string, string>
            {
                ["accessid"] = key.AccessKeyId,
                ["secretkey"] = key.SecretAccessKey,
                ["username"] = key.UserName
            };
        }
    }
}

