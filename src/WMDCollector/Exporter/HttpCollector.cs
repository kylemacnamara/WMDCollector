//using Newtonsoft.Json;
//using System.Net.Http;

using System;
using System.Collections.Generic;
using System.Configuration;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Parsers;
using System.IO.Compression;
using System.Threading;

namespace WMDCollector
{
    public class HttpCollector
    {
        public static readonly string URL = "https://localhost/add/";
        public static readonly string ERROR_URL = "https://localhost/error/";

        // Default GUID that will be replaced by patching
        public static string DEFAULT_GUID = "TO_BE_REPLACED_BY_PATCHER";
        public static string GUID = InitializeGuid();

        public HttpCollector()
        {
            ServicePointManager.ServerCertificateValidationCallback = delegate { return true; };
        }
        private static string InitializeGuid()
        {
            string guid = DEFAULT_GUID;
            if (File.Exists("seclab_secret_guid.txt"))
            {
                guid = File.ReadAllText("seclab_secret_guid.txt").TrimEnd('\r', '\n');
            }
            else
            {
                guid = guid.Replace("\0", string.Empty);

            }
            return guid;
        }
        private void HandleResponse(HttpWebRequest request, Action<HttpWebResponse> responseAction)
        {
            Action wrapperAction = () =>
            {
                request.BeginGetResponse(new AsyncCallback((iar) =>
                {
                    try
                    {
                        var response = (HttpWebResponse)((HttpWebRequest)iar.AsyncState).EndGetResponse(iar);
                        responseAction(response);
                        response.Close();
                    }
                    catch (Exception)
                    {
                    }
                }), request);
            };
            wrapperAction.BeginInvoke(new AsyncCallback((iar) =>
            {
                var action = (Action)iar.AsyncState;
                action.EndInvoke(iar);
            }), wrapperAction);
        }

        // http://msdn.microsoft.com/en-us/library/debx8sh9.aspx
        public void PostError(String stackTrace)
        {
            try
            {
                Console.WriteLine(ERROR_URL);
                // Create a request using a URL that can receive a post. 
                WebRequest request = WebRequest.Create(ERROR_URL + GUID);
                // Set the Method property of the request to POST.
                request.Method = "POST";
                // Create POST data and convert it to a byte array.
                string postData = stackTrace;
                byte[] byteArray = Encoding.UTF8.GetBytes(postData);
                // Set the ContentType property of the WebRequest.
                request.ContentType = "application/x-www-form-urlencoded";
                // Set the ContentLength property of the WebRequest.
                request.ContentLength = byteArray.Length;
                // Get the request stream.
                Stream dataStream = request.GetRequestStream();
                // Write the data to the request stream.
                dataStream.Write(byteArray, 0, byteArray.Length);
                // Close the Stream object.
                dataStream.Close();

                // Get the response.
                WebResponse response = request.GetResponse();
                // Get the stream containing content returned by the server.
                dataStream = response.GetResponseStream();
                // Open the stream using a StreamReader for easy access.
                StreamReader reader = new StreamReader(dataStream);
                // Read the content.
                string responseFromServer = reader.ReadToEnd();
                // Clean up the streams.
                reader.Close();
                dataStream.Close();
                response.Close();
            }
            catch (Exception) { }
        }

        public void Post(String msg)
        {
            try
            {
                byte[] byteData = UTF8Encoding.UTF8.GetBytes(msg);
                Console.WriteLine("Posting {0} bytes", byteData.Length);
                HttpWebRequest request = (HttpWebRequest)WebRequest.Create(URL + GUID);
                request.ServicePoint.Expect100Continue = false;
                request.ServicePoint.ConnectionLimit = 1;
                request.KeepAlive = true;
                request.Method = "POST";
                request.ContentType = "application/x-www-form-urlencoded";
                request.Headers.Add("Content-Encoding", "gzip");

                using (Stream postStream = request.GetRequestStream())
                {
                    using (var zipStream = new GZipStream(postStream, CompressionMode.Compress))
                    {
                        zipStream.Write(byteData, 0, byteData.Length);
                    }
                }

                HandleResponse(request, (response) =>
                {
                    var body = new StreamReader(response.GetResponseStream()).ReadToEnd();
                    Console.Write(body);
                });
                Console.WriteLine("Finished posting!");
            }
            catch (Exception) { }
        }
    }
}

