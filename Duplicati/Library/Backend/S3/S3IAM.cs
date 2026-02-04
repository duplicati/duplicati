// Copyright (C) 2025, The Duplicati Team
// https://duplicati.com, hello@duplicati.com
// 
// Permission is hereby granted, free of charge, to any person obtaining a 
// copy of this software and associated documentation files (the "Software"), 
// to deal in the Software without restriction, including without limitation 
// the rights to use, copy, modify, merge, publish, distribute, sublicense, 
// and/or sell copies of the Software, and to permit persons to whom the 
// Software is furnished to do so, subject to the following conditions:
// 
// The above copyright notice and this permission notice shall be included in 
// all copies or substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS 
// OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, 
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE 
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER 
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING 
// FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER 
// DEALINGS IN THE SOFTWARE.

using Duplicati.Library.Interface;
using Amazon.IdentityManagement;
using Amazon.IdentityManagement.Model;
using Duplicati.Library.Utility;

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

        public string Key => "s3-iamconfig";

        public string DisplayName => Strings.S3IAM.DisplayName;

        public string Description => Strings.S3IAM.Description;


        public IList<ICommandLineArgument> SupportedCommands => new List<ICommandLineArgument>([
                    new CommandLineArgument(KEY_OPERATION, CommandLineArgument.ArgumentType.Enumeration, Strings.S3IAM.OperationShort, Strings.S3IAM.OperationLong, null, Enum.GetNames(typeof(Operation))),
                    new CommandLineArgument(KEY_USERNAME, CommandLineArgument.ArgumentType.String, Strings.S3IAM.UsernameShort, Strings.S3IAM.UsernameLong),
                    new CommandLineArgument(KEY_PASSWORD, CommandLineArgument.ArgumentType.String, Strings.S3IAM.PasswordShort, Strings.S3IAM.PasswordLong)
                ]);

        public async Task<IDictionary<string, string>> Execute(IDictionary<string, string> options, CancellationToken cancellationToken)
        {
            options.TryGetValue(KEY_OPERATION, out var operationstring);
            options.TryGetValue(KEY_USERNAME, out var username);
            options.TryGetValue(KEY_PASSWORD, out var password);
            options.TryGetValue(KEY_PATH, out var path);

            ValidateArgument(operationstring, KEY_OPERATION);

            if (!Enum.TryParse<Operation>(operationstring, true, out var operation))
                throw new ArgumentException(string.Format("Unable to parse {0} as an operation", operationstring));

            switch (operation)
            {
                case Operation.GetPolicyDoc:
                    ValidateArgument(path, KEY_PATH);
                    return GetPolicyDoc(path!);

                case Operation.CreateIAMUser:
                    ValidateArgument(username, KEY_USERNAME);
                    ValidateArgument(password, KEY_PASSWORD);
                    ValidateArgument(path, KEY_PATH);
                    return await CreateUnprivilegedUser(username!, password!, path!, cancellationToken).ConfigureAwait(false);

                default:
                    ValidateArgument(username, KEY_USERNAME);
                    ValidateArgument(password, KEY_PASSWORD);
                    return await CanCreateUser(username!, password!, cancellationToken).ConfigureAwait(false);
            }
        }

        public IDictionary<string, IDictionary<string, string?>> GetLookups()
            => new Dictionary<string, IDictionary<string, string?>>();

        private static void ValidateArgument(string? arg, string type)
        {
            if (string.IsNullOrWhiteSpace(arg))
                throw new ArgumentNullException(type);
        }

        private static IDictionary<string, string> GetPolicyDoc(string path)
            => new Dictionary<string, string>
            {
                ["doc"] = GeneratePolicyDoc(path)
            };

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


        private static async Task<bool> DetermineIfCreateUserIsAllowed(User user, AmazonIdentityManagementServiceClient cl, CancellationToken cancellationToken)
        {
            var simulatePrincipalPolicy = new SimulatePrincipalPolicyRequest
            {
                PolicySourceArn = user.Arn,
                ActionNames = new[] { "iam:CreateUser" }.ToList()
            };

            return (await cl.SimulatePrincipalPolicyAsync(simulatePrincipalPolicy, cancellationToken).ConfigureAwait(false))
                    .EvaluationResults.First()
                    .EvalDecision == PolicyEvaluationDecisionType.Allowed;
        }

        private static async Task<IDictionary<string, string>> GetCreateUserDict(User user, AmazonIdentityManagementServiceClient cl, CancellationToken cancellationToken)
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
                resultDict["isroot"] = (await DetermineIfCreateUserIsAllowed(user, cl, cancellationToken)).ToString();
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

        private static async Task<IDictionary<string, string>> CanCreateUser(string awsid, string awskey, CancellationToken cancellationToken)
        {
            var cl = new AmazonIdentityManagementServiceClient(awsid, awskey);
            User user;
            try
            {
                user = (await cl.GetUserAsync(cancellationToken).ConfigureAwait(false)).User;
            }
            catch (Exception ex)
            {
                return new Dictionary<string, string>
                {
                    ["isroot"] = false.ToString(),
                    ["ex"] = ex.ToString(),
                    ["error"] = $"Exception occurred while retrieving user {awsid} : {ex.Message}"
                };
            }

            return await GetCreateUserDict(user, cl, cancellationToken);
        }

        private static async Task<IDictionary<string, string>> CreateUnprivilegedUser(string awsid, string awskey, string path, CancellationToken cancellationToken)
        {
            var now = Utility.Utility.SerializeDateTime(DateTime.Now);
            var username = string.Format("duplicati-autocreated-backup-user-{0}", now);
            var policyname = string.Format("duplicati-autocreated-policy-{0}", now);
            var policydoc = GeneratePolicyDoc(path);

            var cl = new AmazonIdentityManagementServiceClient(awsid, awskey);
            var user = (await cl.CreateUserAsync(new CreateUserRequest(username), cancellationToken).ConfigureAwait(false)).User;
            await cl.PutUserPolicyAsync(new PutUserPolicyRequest(
                user.UserName,
                policyname,
                policydoc
            )).ConfigureAwait(false);
            var key = (await cl.CreateAccessKeyAsync(new CreateAccessKeyRequest { UserName = user.UserName }, cancellationToken).ConfigureAwait(false)).AccessKey;

            return new Dictionary<string, string>
            {
                ["accessid"] = key.AccessKeyId,
                ["secretkey"] = key.SecretAccessKey,
                ["username"] = key.UserName
            };
        }
    }
}
