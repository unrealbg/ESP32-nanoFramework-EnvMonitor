namespace ESP32_NF_MQTT_DHT.Helpers
{
    using System;
    using System.Collections;
    using System.Diagnostics;
    using System.IO;
    using System.Text;
    using System.Threading;

    using ESP32_NF_MQTT_DHT.OTA;

    public static class CredentialCache
    {
        // Use a different file path that's likely to be writable on ESP32
        private const string CredentialsPath = "I:\\config\\credentials.txt";
        private static readonly object _syncLock = new object();

        private const string Sha256Prefix = "sha256:";
        private const int DefaultSaltBytes = 16; // 128-bit salt

        // Buffer size for file operations
        private const int BufferSize = 256;

        public static string Username { get; set; } = "admin";
        
        /// <summary>
        /// Stores either legacy plaintext password or a SHA-256 encoded password.
        /// Supported formats:
        /// - sha256:&lt;64-hex&gt; (legacy, unsalted)
        /// - sha256:&lt;salt-hex&gt;:&lt;64-hex&gt; (recommended)
        /// </summary>
        public static string PasswordHash { get; set; } = Sha256Prefix + "";
        
        public static void Load()
        {
            try
            {
                if (!File.Exists(CredentialsPath))
                {
                    Debug.WriteLine("Credentials file not found, using defaults");
                    SetDefaultCredentials();
                    return;
                }

                string[] lines = ReadAllLines(CredentialsPath);
                if (lines.Length >= 2 && !string.IsNullOrEmpty(lines[0]) && !string.IsNullOrEmpty(lines[1]))
                {
                    Username = lines[0].Trim();
                    PasswordHash = lines[1].Trim();
                    Debug.WriteLine("Credentials loaded successfully");
                }
                else
                {
                    SetDefaultCredentials();
                    Debug.WriteLine("Invalid credentials format, using defaults");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine("Error loading credentials: " + ex.Message);
                LogHelper.LogError("Failed to load credentials", ex);
                SetDefaultCredentials();
            }
        }

        public static void Update(string username, string password)
        {
            if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
            {
                Debug.WriteLine("Cannot update with empty credentials");
                return;
            }

            lock (_syncLock)
            {
                try
                {
                    // Validate inputs before updating
                    if (username.Length > 50 || password.Length > 100)
                    {
                        Debug.WriteLine("Credentials too long, rejecting update");
                        return;
                    }

                    // Update credentials in memory first
                    Username = username;
                    // Prefer salted hashing (salt is stored alongside the hash)
                    string saltHex = GenerateSaltHex(DefaultSaltBytes);
                    PasswordHash = Sha256Prefix + saltHex + ":" + ComputeSha256Hex(saltHex + ":" + password);
                    Debug.WriteLine("Credentials updated in memory");
                    
                    // Try to persist to file
                    SaveCredentialsToFile(username, password);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine("Error updating credentials: " + ex.Message);
                    LogHelper.LogError("Failed to update credentials", ex);
                    // Don't rethrow - we've already updated the credentials in memory
                }
            }
        }

        public static bool Validate(string username, string password)
        {
            if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
            {
                Debug.WriteLine("Credential validation: Empty credentials provided");
                return false;
            }

            string storedUser;
            string storedSecret;

            lock (_syncLock)
            {
                storedUser = Username;
                storedSecret = PasswordHash;
            }

            if (storedUser != username)
            {
                Debug.WriteLine("Credential validation: Failed");
                return false;
            }

            // New format: sha256:<salt>:<hex> (or legacy sha256:<hex>)
            string saltHex;
            string expectedHex;

            if (TryExtractSha256(storedSecret, out saltHex, out expectedHex))
            {
                bool ok;
                if (!string.IsNullOrEmpty(saltHex))
                {
                    ok = VerifySha256Hex(saltHex + ":" + password, expectedHex);
                }
                else
                {
                    // legacy unsalted sha256
                    ok = VerifySha256Hex(password, expectedHex);
                }

                Debug.WriteLine("Credential validation: " + (ok ? "Success" : "Failed"));

                // If legacy unsalted validated, migrate to salted.
                if (ok && string.IsNullOrEmpty(saltHex))
                {
                    lock (_syncLock)
                    {
                        try
                        {
                            string newSalt = GenerateSaltHex(DefaultSaltBytes);
                            PasswordHash = Sha256Prefix + newSalt + ":" + ComputeSha256Hex(newSalt + ":" + password);
                            SaveCredentialsToFile(Username, password);
                        }
                        catch
                        {
                            // ignore migration failures
                        }
                    }
                }

                return ok;
            }

            // Legacy format: plaintext (backward compatible)
            bool isValidLegacy = storedSecret == password;
            Debug.WriteLine("Credential validation: " + (isValidLegacy ? "Success" : "Failed"));

            // Opportunistic migration: if legacy validated, rewrite file using sha256 format.
            if (isValidLegacy)
            {
                lock (_syncLock)
                {
                    try
                    {
                        string salt = GenerateSaltHex(DefaultSaltBytes);
                        PasswordHash = Sha256Prefix + salt + ":" + ComputeSha256Hex(salt + ":" + password);
                        SaveCredentialsToFile(Username, password);
                    }
                    catch
                    {
                        // ignore migration failures
                    }
                }
            }

            return isValidLegacy;
        }

        private static void SetDefaultCredentials()
        {
            Username = "admin";
            string salt = GenerateSaltHex(DefaultSaltBytes);
            PasswordHash = Sha256Prefix + salt + ":" + ComputeSha256Hex(salt + ":" + "admin");
        }

        private static void SaveCredentialsToFile(string username, string password)
        {
            try
            {
                // Try to create the directory if it doesn't exist
                string directory = Path.GetDirectoryName(CredentialsPath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                    Debug.WriteLine("Created directory: " + directory);
                }

                // Create credentials content
                var contentBuilder = new StringBuilder();
                contentBuilder.Append(username);
                contentBuilder.Append('\n');
                // Persist salted sha256
                string stored = PasswordHash;
                string saltHex;
                string expectedHex;
                if (!TryExtractSha256(stored, out saltHex, out expectedHex) || string.IsNullOrEmpty(saltHex))
                {
                    // If current in-memory secret isn't salted, create a salted form
                    saltHex = GenerateSaltHex(DefaultSaltBytes);
                    expectedHex = ComputeSha256Hex(saltHex + ":" + password);
                    stored = Sha256Prefix + saltHex + ":" + expectedHex;
                }

                contentBuilder.Append(stored);

                // Write atomically to avoid corruption
                string tempPath = CredentialsPath + ".tmp";
                byte[] content = Encoding.UTF8.GetBytes(contentBuilder.ToString());
                
                File.WriteAllBytes(tempPath, content);
                
                // Atomic move (if supported by filesystem)
                if (File.Exists(CredentialsPath))
                {
                    File.Delete(CredentialsPath);
                }
                
                // Simple rename since atomic move may not be available
                File.Move(tempPath, CredentialsPath);
                
                Debug.WriteLine("Credentials saved to file successfully");
            }
            catch (Exception ex)
            {
                Debug.WriteLine("Error saving credentials to file: " + ex.Message);
                LogHelper.LogWarning("Could not persist credentials to file: " + ex.Message);
                
                // Clean up temp file if it exists
                try
                {
                    string tempPath = CredentialsPath + ".tmp";
                    if (File.Exists(tempPath))
                    {
                        File.Delete(tempPath);
                    }
                }
                catch
                {
                    // Ignore cleanup errors
                }
            }
        }

        private static string[] ReadAllLines(string path)
        {
            var lines = new ArrayList();
            byte[] buffer = null;
            
            try
            {
                buffer = File.ReadAllBytes(path);
                string content = new string(Encoding.UTF8.GetChars(buffer));
                
                // Custom string replacement for \r\n to \n since Replace() is not available in nanoFramework
                string normalizedContent = ReplaceCarriageReturns(content);
                string[] lineArray = normalizedContent.Split('\n');
                
                foreach (string line in lineArray)
                {
                    string trimmedLine = line.Trim();
                    if (!string.IsNullOrEmpty(trimmedLine))
                    {
                        lines.Add(trimmedLine);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine("Error reading credentials file: " + ex.Message);
                LogHelper.LogError("Failed to read credentials file", ex);
                return new string[0];
            }
            finally
            {
                // Clear sensitive data from memory
                if (buffer != null)
                {
                    Array.Clear(buffer, 0, buffer.Length);
                }
            }

            // Convert ArrayList to string array
            var result = new string[lines.Count];
            for (int i = 0; i < lines.Count; i++)
            {
                result[i] = (string)lines[i];
            }
            
            return result;
        }

        // Custom implementation of string replace for \r\n to \n since Replace() is not available in nanoFramework
        private static string ReplaceCarriageReturns(string input)
        {
            if (string.IsNullOrEmpty(input))
                return input;

            var sb = new StringBuilder();
            
            for (int i = 0; i < input.Length; i++)
            {
                if (input[i] == '\r' && i + 1 < input.Length && input[i + 1] == '\n')
                {
                    // Skip the \r, the \n will be added in the next iteration
                    continue;
                }
                else
                {
                    sb.Append(input[i]);
                }
            }
            
            return sb.ToString();
        }

        private static string TryExtractSha256Hex(string stored)
        {
            if (string.IsNullOrEmpty(stored))
            {
                return null;
            }

            // Accept sha256:<hex> and sha256=<hex>
            if (stored.StartsWith("sha256:") || stored.StartsWith("sha256="))
            {
                if (stored.Length >= 7 + 64)
                {
                    string hex = stored.Substring(7).Trim();
                    return LooksLikeSha256Hex(hex) ? hex : null;
                }

                return null;
            }

            // Accept bare 64-hex
            if (LooksLikeSha256Hex(stored))
            {
                return stored;
            }

            return null;
        }

        private static bool TryExtractSha256(string stored, out string saltHex, out string hashHex)
        {
            saltHex = null;
            hashHex = null;

            if (string.IsNullOrEmpty(stored))
            {
                return false;
            }

            // Accept sha256=<...> as alias
            if (stored.StartsWith("sha256="))
            {
                stored = "sha256:" + stored.Substring(7);
            }

            if (!stored.StartsWith(Sha256Prefix))
            {
                // Accept bare hash (legacy)
                if (LooksLikeSha256Hex(stored))
                {
                    hashHex = stored;
                    return true;
                }

                return false;
            }

            string payload = stored.Substring(Sha256Prefix.Length).Trim();
            if (string.IsNullOrEmpty(payload))
            {
                return false;
            }

            // If payload contains ':', treat as salt:hash
            int sep = payload.IndexOf(':');
            if (sep > 0)
            {
                string s = payload.Substring(0, sep).Trim();
                string h = (sep + 1 < payload.Length) ? payload.Substring(sep + 1).Trim() : string.Empty;
                if (LooksLikeHex(s) && LooksLikeSha256Hex(h))
                {
                    saltHex = s;
                    hashHex = h;
                    return true;
                }
            }

            // Otherwise treat as unsalted sha256:<hash>
            if (LooksLikeSha256Hex(payload))
            {
                hashHex = payload;
                return true;
            }

            return false;
        }

        private static bool LooksLikeHex(string hex)
        {
            if (string.IsNullOrEmpty(hex))
            {
                return false;
            }

            for (int i = 0; i < hex.Length; i++)
            {
                char c = hex[i];
                bool isDigit = c >= '0' && c <= '9';
                bool isLower = c >= 'a' && c <= 'f';
                bool isUpper = c >= 'A' && c <= 'F';
                if (!isDigit && !isLower && !isUpper)
                {
                    return false;
                }
            }

            return true;
        }

        private static string GenerateSaltHex(int byteCount)
        {
            if (byteCount <= 0)
            {
                byteCount = 8;
            }

            byte[] salt = new byte[byteCount];

            try
            {
                // nanoFramework: Random is available and cheap; this is not a CSPRNG but it's still
                // an improvement over unsalted hashes and avoids extra dependencies.
                var rnd = new Random((int)DateTime.UtcNow.Ticks);
                rnd.NextBytes(salt);
                return ToHexLower(salt);
            }
            finally
            {
                if (salt != null)
                {
                    Array.Clear(salt, 0, salt.Length);
                }
            }
        }

        private static bool LooksLikeSha256Hex(string hex)
        {
            if (string.IsNullOrEmpty(hex) || hex.Length != 64)
            {
                return false;
            }

            for (int i = 0; i < hex.Length; i++)
            {
                char c = hex[i];
                bool isDigit = c >= '0' && c <= '9';
                bool isLower = c >= 'a' && c <= 'f';
                bool isUpper = c >= 'A' && c <= 'F';
                if (!isDigit && !isLower && !isUpper)
                {
                    return false;
                }
            }

            return true;
        }

        private static bool VerifySha256Hex(string password, string expectedHex)
        {
            byte[] bytes = null;
            byte[] hash = null;

            try
            {
                bytes = Encoding.UTF8.GetBytes(password);
                hash = Sha256Lite.ComputeHash(bytes);
                return HexEqualsIgnoreCase(hash, expectedHex);
            }
            catch
            {
                return false;
            }
            finally
            {
                if (bytes != null)
                {
                    Array.Clear(bytes, 0, bytes.Length);
                }

                if (hash != null)
                {
                    Array.Clear(hash, 0, hash.Length);
                }
            }
        }

        private static string ComputeSha256Hex(string input)
        {
            byte[] bytes = null;
            byte[] hash = null;
            try
            {
                bytes = Encoding.UTF8.GetBytes(input);
                hash = Sha256Lite.ComputeHash(bytes);
                return ToHexLower(hash);
            }
            finally
            {
                if (bytes != null)
                {
                    Array.Clear(bytes, 0, bytes.Length);
                }

                if (hash != null)
                {
                    Array.Clear(hash, 0, hash.Length);
                }
            }
        }

        private static string ToHexLower(byte[] bytes)
        {
            if (bytes == null)
            {
                return null;
            }

            var c = new char[bytes.Length * 2];
            int i = 0;
            for (int j = 0; j < bytes.Length; j++)
            {
                byte b = bytes[j];
                c[i++] = GetHex(b >> 4);
                c[i++] = GetHex(b & 0xF);
            }

            return new string(c);
        }

        private static char GetHex(int n)
        {
            return (char)(n < 10 ? ('0' + n) : ('a' + (n - 10)));
        }

        private static bool HexEqualsIgnoreCase(byte[] hash, string expectedHex)
        {
            if (hash == null || string.IsNullOrEmpty(expectedHex))
            {
                return false;
            }

            int n = hash.Length;
            if (expectedHex.Length != n * 2)
            {
                return false;
            }

            for (int i = 0; i < n; i++)
            {
                byte b = hash[i];
                char c1 = expectedHex[i * 2];
                char c2 = expectedHex[i * 2 + 1];
                int hi = HexNibbleToInt(c1);
                int lo = HexNibbleToInt(c2);
                if (hi < 0 || lo < 0)
                {
                    return false;
                }

                byte v = (byte)((hi << 4) | lo);
                if (v != b)
                {
                    return false;
                }
            }

            return true;
        }

        private static int HexNibbleToInt(char c)
        {
            if (c >= '0' && c <= '9')
            {
                return c - '0';
            }

            if (c >= 'A' && c <= 'F')
            {
                return c - 'A' + 10;
            }

            if (c >= 'a' && c <= 'f')
            {
                return c - 'a' + 10;
            }

            return -1;
        }
    }
}
