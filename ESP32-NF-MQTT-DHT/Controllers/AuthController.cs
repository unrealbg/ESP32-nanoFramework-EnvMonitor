namespace ESP32_NF_MQTT_DHT.Controllers
{
    using System;
    using System.Diagnostics;
    using System.IO;
    using System.Net;
    using System.Text;

    using ESP32_NF_MQTT_DHT.Helpers;

    using nanoFramework.WebServer;

    /// <summary>
    /// Controller for handling authentication-related actions such as changing the password.
    /// </summary>
    public class AuthController : BaseController
    {
        private const string ChangePasswordHtml = 
            "<html><head><title>Change Password</title></head><body>" +
            "<form method='post' action='/change-password'>" +
            "Username:<input name='username' value='admin'><br>" +
            "Password:<input type='password' name='password'><br>" +
            "<input type='submit' value='Change'>" +
            "</form></body></html>";

        private const string ErrorHtml = 
            "<html><body><h3>Error: Username and password required</h3><a href='/change-password'>Back</a></body></html>";

        private const string SuccessHtml = 
            "<html><body><h3>Credentials updated successfully.</h3><a href='/'>Home</a></body></html>";

        private const string ProcessingErrorHtml = 
            "<html><body><h3>Error processing request. Try again.</h3><a href='/change-password'>Back</a></body></html>";

        [Route("/change-password")]
        [Method("GET")]
        public void ChangePasswordPage(WebServerEventArgs e)
        {
#if DEBUG
            Debug.WriteLine("GET request for change-password page");
#endif
            
            if (!this.IsAuthenticated(e))
            {
                this.SendUnauthorizedResponse(e);
                return;
            }

            this.SendResponse(e, ChangePasswordHtml, "text/html");
        }

        [Route("/change-password")]
        [Method("POST")]
        public void ChangePassword(WebServerEventArgs e)
        {
#if DEBUG
            Debug.WriteLine("=== Processing POST request for change-password ===");
#endif

            if (!this.IsAuthenticated(e))
            {
#if DEBUG
                Debug.WriteLine("Authentication failed for password change request");
#endif
                this.SendUnauthorizedResponse(e);
                return;
            }

            string body = null;
            try
            {
#if DEBUG
                Debug.WriteLine("Reading request body...");
#endif
                
                string contentLengthHeader = e.Context.Request.Headers["Content-Length"];
                int contentLength = 0;
                if (!string.IsNullOrEmpty(contentLengthHeader))
                {
                    int.TryParse(contentLengthHeader, out contentLength);
                }

#if DEBUG
                Debug.WriteLine("Content-Length: " + contentLength);
#endif

                body = ReadPostDataSafely(e.Context.Request.InputStream, contentLength);

#if DEBUG
                int bodyLength = body != null ? body.Length : 0;
                Debug.WriteLine("Form data received: " + bodyLength + " chars");
                Debug.WriteLine("Raw form data: " + (body ?? "null"));
#endif

                var credentials = this.ParseFormData(body);
#if DEBUG
                string usernameInfo = credentials.Username != null ? credentials.Username : "null";
                int passwordLength = credentials.Password != null ? credentials.Password.Length : 0;
                Debug.WriteLine("Parsed credentials - Username: " + usernameInfo + ", Password length: " + passwordLength);
#endif
                
                if (string.IsNullOrEmpty(credentials.Username) || string.IsNullOrEmpty(credentials.Password))
                {
#if DEBUG
                    Debug.WriteLine("Empty credentials provided - sending error response");
#endif
                    this.SendResponse(e, ErrorHtml, "text/html");
                    return;
                }

#if DEBUG
                Debug.WriteLine("Updating credentials for username: " + credentials.Username);
#endif
                CredentialCache.Update(credentials.Username, credentials.Password);
#if DEBUG
                Debug.WriteLine("Credentials updated successfully - sending success response");
#endif

                this.SendResponse(e, SuccessHtml, "text/html");
#if DEBUG
                Debug.WriteLine("Success response sent");
#endif
            }
            catch (Exception ex)
            {
#if DEBUG
                Debug.WriteLine("ERROR processing form: " + ex.Message);
                Debug.WriteLine("Exception type: " + ex.GetType().Name);
#endif
                LogHelper.LogError("Error processing password change", ex);
                this.SendResponse(e, ProcessingErrorHtml, "text/html");
#if DEBUG
                Debug.WriteLine("Error response sent");
#endif
            }
            finally
            {
#if DEBUG
                Debug.WriteLine("=== Finished processing POST request ===");
#endif
            }
        }

        [Route("/change-success")]
        [Method("GET")]
        public void ChangeSuccess(WebServerEventArgs e)
        {
            if (!this.IsAuthenticated(e))
            {
                this.SendUnauthorizedResponse(e);
                return;
            }

            this.SendSimpleResponse(e, "Password updated successfully!", "/");
        }

        private static string UrlDecode(string value)
        {
            if (value == null)
            {
                return null;
            }

            return UrlDecode(value, 0, value.Length);
        }

        private static string UrlDecode(string value, int start, int length)
        {
            if (value == null)
            {
                return null;
            }

            if (length <= 0)
            {
                return string.Empty;
            }

            var sb = new StringBuilder(length);
            int end = start + length;
            for (int i = start; i < end; i++)
            {
                char c = value[i];
                if (c == '%' && i + 2 < end)
                {
                    int hi = HexToInt(value[i + 1]);
                    int lo = HexToInt(value[i + 2]);
                    if (hi >= 0 && lo >= 0)
                    {
                        sb.Append((char)((hi << 4) | lo));
                        i += 2;
                        continue;
                    }

                    sb.Append('%');
                    continue;
                }

                sb.Append(c == '+' ? ' ' : c);
            }

            return sb.ToString();
        }

        private static int HexToInt(char c)
        {
            if (c >= '0' && c <= '9')
            {
                return c - '0';
            }

            if (c >= 'a' && c <= 'f')
            {
                return 10 + (c - 'a');
            }

            if (c >= 'A' && c <= 'F')
            {
                return 10 + (c - 'A');
            }

            return -1;
        }

        private static string ReadPostDataSafely(Stream inputStream, int contentLength)
        {
#if DEBUG
            Debug.WriteLine("Attempting to read POST data safely");
#endif

            try
            {
                string result = ReadPostData(inputStream, contentLength);
                if (!string.IsNullOrEmpty(result))
                {
#if DEBUG
                    Debug.WriteLine("Method 1 (chunked reading) succeeded");
#endif
                    return result;
                }
            }
            catch (Exception ex)
            {
#if DEBUG
                Debug.WriteLine("Method 1 failed: " + ex.Message);
#endif
            }

            try
            {
#if DEBUG
                Debug.WriteLine("Trying Method 2: StreamReader with timeout");
#endif
                using (var reader = new StreamReader(inputStream))
                {
                    var startTime = DateTime.UtcNow;
                    var timeoutMs = 3000; // 3 seconds max

                    var sb = new StringBuilder();
                    char[] buffer = new char[256];
                    int charsRead;

                    while ((DateTime.UtcNow - startTime).TotalMilliseconds < timeoutMs)
                    {
                        try
                        {
                            charsRead = reader.Read(buffer, 0, buffer.Length);
                            if (charsRead == 0)
                            {
                                break;
                            }

                            sb.Append(buffer, 0, charsRead);
                        }
                        catch (Exception readEx)
                        {
#if DEBUG
                            Debug.WriteLine("Read exception in Method 2: " + readEx.Message);
#endif
                            break;
                        }
                    }

                    string result = sb.ToString();
                    if (!string.IsNullOrEmpty(result))
                    {
#if DEBUG
                        Debug.WriteLine("Method 2 (StreamReader with timeout) succeeded");
#endif
                        return result;
                    }
                }
            }
            catch (Exception ex)
            {
#if DEBUG
                Debug.WriteLine("Method 2 failed: " + ex.Message);
#endif
            }

#if DEBUG
            Debug.WriteLine("All read methods failed, returning empty string");
#endif
            return string.Empty;
        }

        private static string ReadPostData(Stream inputStream, int contentLength)
        {
            try
            {
#if DEBUG
                Debug.WriteLine("Reading POST data with content length: " + contentLength);
#endif

                // Set a reasonable timeout for the stream operation
                if (inputStream.CanTimeout)
                {
                    inputStream.ReadTimeout = 5000; // 5 seconds timeout
#if DEBUG
                    Debug.WriteLine("Set stream read timeout to 5 seconds");
#endif
                }

                if (contentLength <= 0 || contentLength > 4096)
                {
#if DEBUG
                    Debug.WriteLine("Invalid content length, attempting to read with buffer");
#endif
                    contentLength = 1024;
                }

                byte[] buffer = new byte[contentLength];
                int totalBytesRead = 0;
                int attempts = 0;
                const int maxAttempts = 10;

                while (totalBytesRead < contentLength && attempts < maxAttempts)
                {
                    attempts++;
                    int bytesToRead = Math.Min(256, contentLength - totalBytesRead);

                    try
                    {
                        int bytesRead = inputStream.Read(buffer, totalBytesRead, bytesToRead);

                        if (bytesRead == 0)
                        {
#if DEBUG
                            Debug.WriteLine("No more data available, breaking read loop at attempt " + attempts);
#endif
                            break;
                        }

                        totalBytesRead += bytesRead;
#if DEBUG
                        Debug.WriteLine("Attempt " + attempts + ": Read " + bytesRead + " bytes, total: " + totalBytesRead);
#endif
                    }
                    catch (IOException ioEx)
                    {
#if DEBUG
                        Debug.WriteLine("IO Exception during read attempt " + attempts + ": " + ioEx.Message);
#endif
                        break;
                    }
                    catch (System.Net.Sockets.SocketException sockEx)
                    {
#if DEBUG
                        Debug.WriteLine("Socket Exception during read attempt " + attempts + ": " + sockEx.Message);
#endif
                        break;
                    }
                }

                if (totalBytesRead > 0)
                {
                    string result = Encoding.UTF8.GetString(buffer, 0, totalBytesRead);
#if DEBUG
                    Debug.WriteLine("Successfully read POST data: " + totalBytesRead + " bytes");
#endif
                    return result;
                }
                else
                {
#if DEBUG
                    Debug.WriteLine("No POST data read after " + attempts + " attempts");
#endif
                    return string.Empty;
                }
            }
            catch (Exception ex)
            {
#if DEBUG
                Debug.WriteLine("Error reading POST data: " + ex.Message);
                Debug.WriteLine("Exception type: " + ex.GetType().Name);
#endif
                return string.Empty;
            }
        }

        private CredentialPair ParseFormData(string body)
        {
            if (string.IsNullOrEmpty(body))
            {
                return new CredentialPair(null, null);
            }

            string username = null;
            string password = null;

            // Fast single-pass parser for application/x-www-form-urlencoded
            // Expected keys: username, password
            int i = 0;
            int len = body.Length;
            while (i < len)
            {
                int keyStart = i;
                int keyEnd = -1;
                int valueStart = -1;

                // Find '=' or '&'
                for (; i < len; i++)
                {
                    char c = body[i];
                    if (c == '=')
                    {
                        keyEnd = i;
                        valueStart = i + 1;
                        i++;
                        break;
                    }
                    if (c == '&')
                    {
                        // Key without value
                        keyEnd = i;
                        valueStart = -1;
                        i++;
                        break;
                    }
                }

                if (keyEnd < 0)
                {
                    keyEnd = len;
                }

                int keyLength = keyEnd - keyStart;

                // Find end of value (or end)
                int valueEnd = len;
                if (valueStart >= 0)
                {
                    for (; i < len; i++)
                    {
                        if (body[i] == '&')
                        {
                            valueEnd = i;
                            i++;
                            break;
                        }
                    }

                    int valueLength = valueEnd - valueStart;
                    if (valueLength < 0)
                    {
                        valueLength = 0;
                    }

                    if (keyLength == 8 && MatchesKey(body, keyStart, "username"))
                    {
                        username = UrlDecode(body, valueStart, valueLength);
                    }
                    else if (keyLength == 8 && MatchesKey(body, keyStart, "password"))
                    {
                        password = UrlDecode(body, valueStart, valueLength);
                    }
                }

                if (username != null && password != null)
                {
                    break;
                }

                // If we reached end while scanning for '='
                if (i >= len)
                {
                    break;
                }
            }

            return new CredentialPair(username, password);
        }

        private static bool MatchesKey(string source, int start, string expected)
        {
            if (expected == null)
            {
                return false;
            }

            if (start < 0 || (start + expected.Length) > source.Length)
            {
                return false;
            }

            for (int i = 0; i < expected.Length; i++)
            {
                if (source[start + i] != expected[i])
                {
                    return false;
                }
            }

            return true;
        }
        
        private void SendSimpleResponse(WebServerEventArgs e, string message, string backLink)
        {
            string html = "<html><body><h3>" + message + "</h3><a href='" + backLink + "'>Back</a></body></html>";
            
            this.SendResponse(e, html, "text/html");
        }
        
        private void SendRedirectResponse(WebServerEventArgs e, string location)
        {
            try
            {
                HttpListenerResponse response = e.Context.Response;
                response.StatusCode = (int)HttpStatusCode.Redirect;
                response.Headers.Add("Location", location);
                response.Close();
#if DEBUG
                Debug.WriteLine("Redirect sent to " + location);
#endif
            }
            catch (Exception ex)
            {
#if DEBUG
                Debug.WriteLine("Error sending redirect: " + ex.Message);
#endif
                LogHelper.LogError("Error sending redirect", ex);
            }
        }
        

        private struct CredentialPair
        {
            public string Username;
            public string Password;

            public CredentialPair(string username, string password)
            {
                this.Username = username;
                this.Password = password;
            }
        }
    }
}
