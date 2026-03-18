namespace ESP32_NF_MQTT_DHT.Controllers
{
    using System;
    using System.Collections;
    using System.Diagnostics;
    using System.Net;
    using System.Text;

    using ESP32_NF_MQTT_DHT.Helpers;

    using nanoFramework.WebServer;

    /// <summary>
    /// Abstract base class for handling common web server functionalities such as authentication,
    /// request throttling, and response handling.
    /// </summary>
    public abstract class BaseController
    {
        private static readonly Hashtable RequestTimesByEndpoint = new Hashtable();
        private static readonly Hashtable BanList = new Hashtable();
        private static readonly TimeSpan RequestInterval = TimeSpan.FromMilliseconds(200);
        private static readonly TimeSpan BanDuration = TimeSpan.FromMinutes(5);
        private static readonly object SyncLock = new object();

        private const int MaxTrackedRequestKeys = 128;
        private const int MaxBannedClients = 64;
        private const int SweepEveryNRequests = 64;
        private static int _requestCounter;

        protected void SendPage(WebServerEventArgs e, string page)
        {
            this.SendResponse(e, page, "text/html");
        }

        protected bool IsAuthenticated(WebServerEventArgs e)
        {
            var authHeader = e.Context.Request.Headers["Authorization"];
            return authHeader != null && this.ValidateAuthHeader(authHeader);
        }

        protected void SendUnauthorizedResponse(WebServerEventArgs e)
        {
            try
            {
                e.Context.Response.StatusCode = (int)HttpStatusCode.Unauthorized;
                e.Context.Response.ContentType = "text/html";
                e.Context.Response.Headers.Add("WWW-Authenticate", "Basic realm=\"ESP32 Device Access\"");
                WebServer.OutPutStream(e.Context.Response, "Authentication required");
                LogHelper.LogWarning("Authentication required for access");
            }
            catch (Exception ex)
            {
                LogHelper.LogError("Error sending unauthorized response", ex);
            }
        }

        protected void SendNotFoundResponse(WebServerEventArgs e)
        {
            try
            {
                string responseMessage = "The requested resource was not found.";
                e.Context.Response.StatusCode = (int)HttpStatusCode.NotFound;
                e.Context.Response.ContentType = "text/plain";
                WebServer.OutPutStream(e.Context.Response, responseMessage);
                
                string remoteEndpoint = e.Context.Request.RemoteEndPoint != null ? 
                    e.Context.Request.RemoteEndPoint.ToString() : "Unknown";
                Debug.WriteLine("Not Found response sent to " + remoteEndpoint);
                LogHelper.LogError("Resource not found.");
            }
            catch (Exception ex)
            {
                LogHelper.LogError("Error sending not found response", ex);
            }
        }

        protected void SendErrorResponse(WebServerEventArgs e, string logMessage, HttpStatusCode statusCode)
        {
            try
            {
                var clientMessage = "An error occurred. Please try again later.";
                e.Context.Response.StatusCode = (int)statusCode;
                
                string jsonResponse = "{\"error\": \"" + clientMessage + "\"}";
                
                this.SendResponse(e, jsonResponse, "application/json");
                LogHelper.LogError(logMessage);
            }
            catch (Exception ex)
            {
                LogHelper.LogError("Error sending error response", ex);
            }
        }

        protected void SendResponse(WebServerEventArgs e, string content, string contentType = "application/json", HttpStatusCode statusCode = HttpStatusCode.OK)
        {
            HttpListenerResponse response = null;
            try
            {
#if DEBUG
                Debug.WriteLine("Sending response with content type: " + contentType + ", status: " + statusCode);
#endif
                response = e.Context.Response;
                response.StatusCode = (int)statusCode;
                response.ContentType = contentType;
                
                WebServer.OutPutStream(response, content);
#if DEBUG
                Debug.WriteLine("Response sent successfully");
#endif
            }
            catch (System.Net.Sockets.SocketException sockEx)
            {
#if DEBUG
                Debug.WriteLine("SocketException while sending response: " + sockEx.Message);
#endif
                LogHelper.LogError("SocketException while sending response: " + sockEx.Message);
            }
            catch (ObjectDisposedException)
            {
#if DEBUG
                Debug.WriteLine("Response object was already disposed");
#endif
                LogHelper.LogWarning("Response object was already disposed");
            }
            catch (Exception ex)
            {
#if DEBUG
                Debug.WriteLine("Failed to send response: " + ex.Message);
#endif
                LogHelper.LogError("Failed to send response: " + ex.Message);
            }
        }

        protected void HandleRequest(WebServerEventArgs e, Action action, string endpoint)
        {
            if (!this.IsAuthenticated(e))
            {
                this.SendUnauthorizedResponse(e);
                return;
            }

            string clientIp = e.Context.Request.RemoteEndPoint != null && e.Context.Request.RemoteEndPoint.Address != null ? 
                             e.Context.Request.RemoteEndPoint.Address.ToString() : "Unknown";

            var nowUtc = DateTime.UtcNow;

            lock (SyncLock)
            {
                // Periodically clean up old entries to keep memory stable.
                SweepIfNeeded(nowUtc);

                EnforceCaps();

                if (this.IsBanned(clientIp, nowUtc))
                {
                    this.SendForbiddenResponse(e);
                    return;
                }

                if (this.ShouldThrottle(clientIp, endpoint, nowUtc))
                {
                    this.BanClient(clientIp, nowUtc);
                    this.SendThrottleResponse(e);
                    return;
                }

                this.CleanupOldRequests(clientIp, endpoint, nowUtc);
            }

            try
            {
                action.Invoke();
                this.UpdateLastRequestTime(clientIp, endpoint, nowUtc);
            }
            catch (Exception ex)
            {
                LogHelper.LogError("Error handling request", ex);
                this.SendErrorResponse(e, "Request processing failed", HttpStatusCode.InternalServerError);
            }
        }

        protected void SendForbiddenResponse(WebServerEventArgs e)
        {
            try
            {
                string responseMessage = "Your access is temporarily suspended due to excessive requests.";
                e.Context.Response.StatusCode = (int)HttpStatusCode.Forbidden;
                e.Context.Response.ContentType = "text/plain";
                WebServer.OutPutStream(e.Context.Response, responseMessage);
                LogHelper.LogWarning("Access forbidden.");
            }
            catch (Exception ex)
            {
                LogHelper.LogError("Error sending forbidden response", ex);
            }
        }

        protected void SendThrottleResponse(WebServerEventArgs e)
        {
            try
            {
                string responseMessage = "Too many requests. You have been temporarily banned. Please wait 5 minutes.";
                e.Context.Response.StatusCode = (int)HttpStatusCode.BadRequest;
                e.Context.Response.ContentType = "text/plain";
                WebServer.OutPutStream(e.Context.Response, responseMessage);
                LogHelper.LogWarning("Request throttled.");
            }
            catch (Exception ex)
            {
                LogHelper.LogError("Error sending throttle response", ex);
            }
        }

        private static string[] ReadAllLines(string path)
        {
            var lines = new System.Collections.ArrayList();
            System.IO.FileStream fs = null;
            System.IO.StreamReader reader = null;

            try
            {
                fs = new System.IO.FileStream(path, System.IO.FileMode.Open, System.IO.FileAccess.Read);
                reader = new System.IO.StreamReader(fs);

                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    lines.Add(line);
                }
            }
            finally
            {
                if (reader != null)
                {
                    reader.Dispose();
                }
                if (fs != null)
                {
                    fs.Dispose();
                }
            }

            var arr = new string[lines.Count];
            for (int i = 0; i < lines.Count; i++)
            {
                arr[i] = (string)lines[i];
            }

            return arr;
        }

        private void CleanupOldRequests(string clientIp, string endpoint, DateTime nowUtc)
        {
            try
            {
                if (clientIp == "Unknown")
                {
                    return;
                }

                string key = clientIp + "_" + endpoint;
                DateTime threshold = nowUtc.AddMinutes(-5);

                var requestTimeObj = RequestTimesByEndpoint[key];
                if (requestTimeObj != null)
                {
                    DateTime requestTime = (DateTime)requestTimeObj;
                    if (requestTime < threshold)
                    {
                        RequestTimesByEndpoint.Remove(key);
                    }
                }
            }
            catch (Exception ex)
            {
                LogHelper.LogError("Error during cleanup of old requests", ex);
            }
        }

        private static void SweepIfNeeded(DateTime nowUtc)
        {
            _requestCounter++;
            if ((_requestCounter % SweepEveryNRequests) != 0)
            {
                return;
            }

            try
            {
                if (BanList.Count > 0)
                {
                    var toRemove = new ArrayList();
                    var banEnumerator = BanList.GetEnumerator();
                    while (banEnumerator.MoveNext())
                    {
                        var entry = (DictionaryEntry)banEnumerator.Current;
                        var banEnd = (DateTime)entry.Value;
                        if (nowUtc > banEnd)
                        {
                            toRemove.Add(entry.Key);
                        }
                    }

                    for (int i = 0; i < toRemove.Count; i++)
                    {
                        BanList.Remove(toRemove[i]);
                    }
                }

                if (RequestTimesByEndpoint.Count > 0)
                {
                    DateTime threshold = nowUtc.AddMinutes(-5);
                    var toRemove = new ArrayList();
                    var reqEnumerator = RequestTimesByEndpoint.GetEnumerator();
                    while (reqEnumerator.MoveNext())
                    {
                        var entry = (DictionaryEntry)reqEnumerator.Current;
                        var lastRequest = (DateTime)entry.Value;
                        if (lastRequest < threshold)
                        {
                            toRemove.Add(entry.Key);
                        }
                    }

                    for (int i = 0; i < toRemove.Count; i++)
                    {
                        RequestTimesByEndpoint.Remove(toRemove[i]);
                    }
                }
            }
            catch (Exception ex)
            {
                LogHelper.LogError("Error sweeping request/ban tables", ex);
            }
        }

        private static void EnforceCaps()
        {
            try
            {
                if (RequestTimesByEndpoint.Count > MaxTrackedRequestKeys)
                {
                    RequestTimesByEndpoint.Clear();
                    LogHelper.LogWarning("Request tracking table cleared due to size cap.");
                }

                if (BanList.Count > MaxBannedClients)
                {
                    BanList.Clear();
                    LogHelper.LogWarning("Ban list cleared due to size cap.");
                }
            }
            catch (Exception ex)
            {
                LogHelper.LogError("Error enforcing caps for request/ban tables", ex);
            }
        }

        private bool IsBanned(string clientIp, DateTime nowUtc)
        {
            try
            {
                if (clientIp == "Unknown")
                {
                    return false;
                }

                var banEndTimeObj = BanList[clientIp];
                if (banEndTimeObj != null)
                {
                    DateTime banEndTime = (DateTime)banEndTimeObj;
                    if (nowUtc <= banEndTime)
                    {
                        Debug.WriteLine("Access denied for " + clientIp + ". Still banned.");
                        return true;
                    }

                    BanList.Remove(clientIp);
                }

                return false;
            }
            catch (Exception ex)
            {
                LogHelper.LogError("Error checking ban status", ex);
                return false;
            }
        }

        private bool ShouldThrottle(string clientIp, string endpoint, DateTime nowUtc)
        {
            try
            {
                if (clientIp == "Unknown")
                {
                    return false;
                }

                string key = clientIp + "_" + endpoint;

                var lastRequestTimeObj = RequestTimesByEndpoint[key];
                if (lastRequestTimeObj != null)
                {
                    DateTime lastRequestTime = (DateTime)lastRequestTimeObj;
                    return nowUtc - lastRequestTime < RequestInterval;
                }

                return false;
            }
            catch (Exception ex)
            {
                LogHelper.LogError("Error checking throttle status", ex);
                return false;
            }
        }

        private void BanClient(string clientIp, DateTime nowUtc)
        {
            try
            {
                if (clientIp == "Unknown")
                {
                    return;
                }

                DateTime banUntil = nowUtc.Add(BanDuration);
                BanList[clientIp] = banUntil;
                LogHelper.LogWarning("Client " + clientIp + " has been banned until " + banUntil.ToString());
            }
            catch (Exception ex)
            {
                LogHelper.LogError("Error banning client", ex);
            }
        }

        private void UpdateLastRequestTime(string clientIp, string endpoint, DateTime nowUtc)
        {
            try
            {
                if (clientIp == "Unknown")
                {
                    return;
                }

                string key = clientIp + "_" + endpoint;
                RequestTimesByEndpoint[key] = nowUtc;
            }
            catch (Exception ex)
            {
                LogHelper.LogError("Error updating request time", ex);
            }
        }

        private bool ValidateAuthHeader(string authHeader)
        {
            try
            {
                if (string.IsNullOrEmpty(authHeader) || !authHeader.StartsWith("Basic "))
                {
                    return false;
                }

                var encodedCredentials = authHeader.Substring("Basic ".Length).Trim();
                var decodedBytes = Convert.FromBase64String(encodedCredentials);
                var credentials = Encoding.UTF8.GetString(decodedBytes, 0, decodedBytes.Length);

                int separatorIndex = credentials.IndexOf(':');
                if (separatorIndex <= 0 || separatorIndex >= credentials.Length - 1)
                {
                    return false;
                }

                var username = credentials.Substring(0, separatorIndex);
                var password = credentials.Substring(separatorIndex + 1);

                return CredentialCache.Validate(username, password);
            }
            catch (Exception ex)
            {
                LogHelper.LogError("Error validating credentials", ex);
                return false;
            }
        }
    }
}
