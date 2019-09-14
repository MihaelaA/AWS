using System;
using System.Threading.Tasks;
using System.Net;
using System.IO;
using System.Text;

using Amazon.Lambda.Core;
using Amazon.Lambda.S3Events;
using Amazon.S3;
using Amazon.S3.Model;

using Amazon.KeyManagementService;
using Amazon.KeyManagementService.Model;


// Assembly attribute to enable the Lambda function's JSON input to be converted into a .NET class.
[assembly: LambdaSerializer(typeof(Amazon.Lambda.Serialization.Json.JsonSerializer))]

namespace HelloLambda
{
    public class Function
    {
        IAmazonS3 S3Client { get; set; }

        private static string _ftpAddress;
        private static string _user;
        private static string _password;

        private static async Task<string> DecodeEnvVar(string envVarName)
        {
            // Retrieve env var text
            var encryptedBase64Text = Environment.GetEnvironmentVariable(envVarName);
            // Convert base64-encoded text to bytes
            var encryptedBytes = Convert.FromBase64String(encryptedBase64Text);
            // Construct client
            using (var client = new AmazonKeyManagementServiceClient())
            {
                // Construct request
                var decryptRequest = new DecryptRequest
                {
                    CiphertextBlob = new MemoryStream(encryptedBytes),
                };
                // Call KMS to decrypt data
                var response = await client.DecryptAsync(decryptRequest);
                using (var plaintextStream = response.Plaintext)
                {
                    // Get decrypted bytes
                    var plaintextBytes = plaintextStream.ToArray();
                    // Convert decrypted bytes to ASCII text
                    var plaintext = Encoding.UTF8.GetString(plaintextBytes);
                    return plaintext;
                }
            }
        }

        /// <summary>
        /// Default constructor. This constructor is used by Lambda to construct the instance. When invoked in a Lambda environment
        /// the AWS credentials will come from the IAM role associated with the function and the AWS region will be set to the
        /// region the Lambda function is executed in.
        /// </summary>
        public Function()
        {
            S3Client = new AmazonS3Client();

            // Read values once, in the constructor
            _ftpAddress = $"ftp://{Environment.GetEnvironmentVariable("ip")}{Environment.GetEnvironmentVariable("remoteDirectory")}";
            _user = Environment.GetEnvironmentVariable("user");

            // Decrypt code should run once and variables stored outside of the
            // function handler so that these are decrypted once per container
            _password = DecodeEnvVar("password").Result;
        }

        /// <summary>
        /// Constructs an instance with a preconfigured S3 client. This can be used for testing the outside of the Lambda environment.
        /// </summary>
        /// <param name="s3Client"></param>
        public Function(IAmazonS3 s3Client, string ip, string user, string password)
        {
            this.S3Client = s3Client;
            _ftpAddress = ip;
            _user = user;
            _password = password;
        }

        /// <summary>
        /// This method is called for every Lambda invocation. This method takes in an S3 event object and can be used 
        /// to respond to S3 notifications.
        /// </summary>
        /// <param name="evnt"></param>
        /// <param name="context"></param>
        /// <returns></returns>
        public async Task<string> FunctionHandler(S3Event evnt, ILambdaContext context)
        {
            var s3Event = evnt.Records?[0].S3;
            if (s3Event == null)
            {
                return null;
            }

            string result = "unfinished";
            try
            {
                // Get the object used to communicate with the server.
                FtpWebRequest request = (FtpWebRequest)WebRequest.Create($"{_ftpAddress}/{s3Event.Object.Key}");
                request.Method = WebRequestMethods.Ftp.UploadFile;

                // This example assumes the FTP site uses anonymous logon.
                request.Credentials = new NetworkCredential(_user, _password);
                request.UsePassive = true;
                request.UseBinary = true;
                request.KeepAlive = true;

                // Copy the contents of the file to the request stream.
                using (GetObjectResponse s3GetResponse = await this.S3Client.GetObjectAsync(s3Event.Bucket.Name, s3Event.Object.Key))
                {
                    using (StreamReader sr = new StreamReader(s3GetResponse.ResponseStream))
                    {
                        using (Stream requestStream = request.GetRequestStream())
                        {
                            sr.BaseStream.CopyTo(requestStream);
                        }
                    }
                }

                using (FtpWebResponse response = (FtpWebResponse)request.GetResponse())
                {
                    result = $"Upload File Complete, status {response.StatusDescription}";
                    if (context != null)
                        context.Logger.LogLine(result);
                }

                return result;
            }
            catch (Exception e)
            {
                if (context != null)
                {
                    context.Logger.LogLine($"Error getting object {s3Event.Object.Key} from bucket {s3Event.Bucket.Name}. Make sure they exist and your bucket is in the same region as this function.");
                    context.Logger.LogLine(e.Message);
                    context.Logger.LogLine(e.StackTrace);
                }
                throw;
            }
        }
    }
}
