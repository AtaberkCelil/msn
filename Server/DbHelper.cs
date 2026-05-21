using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using MySqlConnector;

namespace Server
{
    public class User
    {
        public int Id { get; set; }
        public string Email { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public string Status { get; set; } = "Offline";
        public string CustomSign { get; set; } = string.Empty;
        public string AvatarFilename { get; set; } = string.Empty;
        public string RelationType { get; set; } = "friend"; // populated for contacts
    }

    public class DbHelper
    {
        private readonly string _connectionString;

        public DbHelper(string connectionString)
        {
            _connectionString = connectionString;
        }

        public bool TestConnection(out string error)
        {
            error = string.Empty;
            try
            {
                using (var conn = new MySqlConnection(_connectionString))
                {
                    conn.Open();
                    return true;
                }
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        private string HashPassword(string password, string salt)
        {
            using (SHA256 sha256 = SHA256.Create())
            {
                byte[] bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(password + salt));
                StringBuilder builder = new StringBuilder();
                foreach (byte b in bytes)
                {
                    builder.Append(b.ToString("x2"));
                }
                return builder.ToString();
            }
        }

        private string GenerateSalt()
        {
            byte[] saltBytes = new byte[16];
            RandomNumberGenerator.Fill(saltBytes);
            return Convert.ToBase64String(saltBytes);
        }

        public bool RegisterUser(string email, string password, string displayName, out string error)
        {
            error = string.Empty;
            email = email.Trim().ToLower();

            if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password) || string.IsNullOrWhiteSpace(displayName))
            {
                error = "All fields are required.";
                return false;
            }

            try
            {
                using (var conn = new MySqlConnection(_connectionString))
                {
                    conn.Open();

                    // Check duplicate
                    string checkSql = "SELECT COUNT(*) FROM users WHERE email = @Email";
                    using (var checkCmd = new MySqlCommand(checkSql, conn))
                    {
                        checkCmd.Parameters.AddWithValue("@Email", email);
                        long count = (long)checkCmd.ExecuteScalar();
                        if (count > 0)
                        {
                            error = "Email is already registered.";
                            return false;
                        }
                    }

                    // Hash password
                    string salt = GenerateSalt();
                    string hash = HashPassword(password, salt);
                    string passwordDbValue = $"{salt}:{hash}";

                    // Insert user
                    string insertSql = "INSERT INTO users (email, password_hash, display_name) VALUES (@Email, @PasswordHash, @DisplayName)";
                    using (var insertCmd = new MySqlCommand(insertSql, conn))
                    {
                        insertCmd.Parameters.AddWithValue("@Email", email);
                        insertCmd.Parameters.AddWithValue("@PasswordHash", passwordDbValue);
                        insertCmd.Parameters.AddWithValue("@DisplayName", displayName);
                        insertCmd.ExecuteNonQuery();
                    }
                    return true;
                }
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        public User? AuthenticateUser(string email, string password)
        {
            email = email.Trim().ToLower();
            try
            {
                using (var conn = new MySqlConnection(_connectionString))
                {
                    conn.Open();
                    string sql = "SELECT id, email, password_hash, display_name, status, custom_sign, avatar_filename FROM users WHERE email = @Email";
                    using (var cmd = new MySqlCommand(sql, conn))
                    {
                        cmd.Parameters.AddWithValue("@Email", email);
                        using (var reader = cmd.ExecuteReader())
                        {
                            if (reader.Read())
                            {
                                string storedDbVal = reader.GetString("password_hash");
                                string[] parts = storedDbVal.Split(':');
                                if (parts.Length != 2) return null;

                                string salt = parts[0];
                                string hash = parts[1];
                                string computedHash = HashPassword(password, salt);

                                if (computedHash == hash)
                                {
                                    return new User
                                    {
                                        Id = reader.GetInt32("id"),
                                        Email = reader.GetString("email"),
                                        DisplayName = reader.GetString("display_name"),
                                        Status = reader.IsDBNull(reader.GetOrdinal("status")) ? "Offline" : reader.GetString("status"),
                                        CustomSign = reader.IsDBNull(reader.GetOrdinal("custom_sign")) ? "" : reader.GetString("custom_sign"),
                                        AvatarFilename = reader.IsDBNull(reader.GetOrdinal("avatar_filename")) ? "" : reader.GetString("avatar_filename")
                                    };
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[DB Error] AuthenticateUser failed: {ex.Message}");
            }
            return null;
        }

        public bool UpdateStatus(int userId, string status)
        {
            try
            {
                using (var conn = new MySqlConnection(_connectionString))
                {
                    conn.Open();
                    string sql = "UPDATE users SET status = @Status WHERE id = @Id";
                    using (var cmd = new MySqlCommand(sql, conn))
                    {
                        cmd.Parameters.AddWithValue("@Status", status);
                        cmd.Parameters.AddWithValue("@Id", userId);
                        cmd.ExecuteNonQuery();
                    }
                    return true;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[DB Error] UpdateStatus failed: {ex.Message}");
                return false;
            }
        }

        public bool UpdateCustomSign(int userId, string customSign)
        {
            try
            {
                using (var conn = new MySqlConnection(_connectionString))
                {
                    conn.Open();
                    string sql = "UPDATE users SET custom_sign = @CustomSign WHERE id = @Id";
                    using (var cmd = new MySqlCommand(sql, conn))
                    {
                        cmd.Parameters.AddWithValue("@CustomSign", customSign);
                        cmd.Parameters.AddWithValue("@Id", userId);
                        cmd.ExecuteNonQuery();
                    }
                    return true;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[DB Error] UpdateCustomSign failed: {ex.Message}");
                return false;
            }
        }

        public bool UpdateAvatar(int userId, string avatarFilename)
        {
            try
            {
                using (var conn = new MySqlConnection(_connectionString))
                {
                    conn.Open();
                    string sql = "UPDATE users SET avatar_filename = @Avatar WHERE id = @Id";
                    using (var cmd = new MySqlCommand(sql, conn))
                    {
                        cmd.Parameters.AddWithValue("@Avatar", avatarFilename);
                        cmd.Parameters.AddWithValue("@Id", userId);
                        cmd.ExecuteNonQuery();
                    }
                    return true;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[DB Error] UpdateAvatar failed: {ex.Message}");
                return false;
            }
        }

        public User? AddContact(int userId, string contactEmail, out string error)
        {
            error = string.Empty;
            contactEmail = contactEmail.Trim().ToLower();

            try
            {
                using (var conn = new MySqlConnection(_connectionString))
                {
                    conn.Open();

                    // Find contact
                    string findSql = "SELECT id, email, display_name, status, custom_sign, avatar_filename FROM users WHERE email = @Email";
                    User? contact = null;
                    using (var findCmd = new MySqlCommand(findSql, conn))
                    {
                        findCmd.Parameters.AddWithValue("@Email", contactEmail);
                        using (var reader = findCmd.ExecuteReader())
                        {
                            if (reader.Read())
                            {
                                contact = new User
                                {
                                    Id = reader.GetInt32("id"),
                                    Email = reader.GetString("email"),
                                    DisplayName = reader.GetString("display_name"),
                                    Status = reader.IsDBNull(reader.GetOrdinal("status")) ? "Offline" : reader.GetString("status"),
                                    CustomSign = reader.IsDBNull(reader.GetOrdinal("custom_sign")) ? "" : reader.GetString("custom_sign"),
                                    AvatarFilename = reader.IsDBNull(reader.GetOrdinal("avatar_filename")) ? "" : reader.GetString("avatar_filename")
                                };
                            }
                        }
                    }

                    if (contact == null)
                    {
                        error = "User not found with that email.";
                        return null;
                    }

                    if (contact.Id == userId)
                    {
                        error = "You cannot add yourself as a contact.";
                        return null;
                    }

                    // Check existing contact relationship
                    string checkSql = "SELECT COUNT(*) FROM contacts WHERE user_id = @UserId AND contact_id = @ContactId";
                    using (var checkCmd = new MySqlCommand(checkSql, conn))
                    {
                        checkCmd.Parameters.AddWithValue("@UserId", userId);
                        checkCmd.Parameters.AddWithValue("@ContactId", contact.Id);
                        long count = (long)checkCmd.ExecuteScalar();
                        if (count > 0)
                        {
                            error = "Contact is already in your contact list.";
                            return null;
                        }
                    }

                    // Add relationship
                    string insertSql = "INSERT INTO contacts (user_id, contact_id, relation_type) VALUES (@UserId, @ContactId, 'friend')";
                    using (var insertCmd = new MySqlCommand(insertSql, conn))
                    {
                        insertCmd.Parameters.AddWithValue("@UserId", userId);
                        insertCmd.Parameters.AddWithValue("@ContactId", contact.Id);
                        insertCmd.ExecuteNonQuery();
                    }

                    return contact;
                }
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return null;
            }
        }

        public bool RemoveContact(int userId, int contactId)
        {
            try
            {
                using (var conn = new MySqlConnection(_connectionString))
                {
                    conn.Open();
                    string sql = "DELETE FROM contacts WHERE user_id = @UserId AND contact_id = @ContactId";
                    using (var cmd = new MySqlCommand(sql, conn))
                    {
                        cmd.Parameters.AddWithValue("@UserId", userId);
                        cmd.Parameters.AddWithValue("@ContactId", contactId);
                        cmd.ExecuteNonQuery();
                    }
                    return true;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[DB Error] RemoveContact failed: {ex.Message}");
                return false;
            }
        }

        public bool BlockContact(int userId, int contactId, bool block)
        {
            try
            {
                using (var conn = new MySqlConnection(_connectionString))
                {
                    conn.Open();
                    string sql = "UPDATE contacts SET relation_type = @RelationType WHERE user_id = @UserId AND contact_id = @ContactId";
                    using (var cmd = new MySqlCommand(sql, conn))
                    {
                        cmd.Parameters.AddWithValue("@RelationType", block ? "blocked" : "friend");
                        cmd.Parameters.AddWithValue("@UserId", userId);
                        cmd.Parameters.AddWithValue("@ContactId", contactId);
                        cmd.ExecuteNonQuery();
                    }
                    return true;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[DB Error] BlockContact failed: {ex.Message}");
                return false;
            }
        }

        public List<User> GetContacts(int userId)
        {
            var contacts = new List<User>();
            try
            {
                using (var conn = new MySqlConnection(_connectionString))
                {
                    conn.Open();
                    string sql = @"
                        SELECT u.id, u.email, u.display_name, u.status, u.custom_sign, u.avatar_filename, c.relation_type 
                        FROM contacts c
                        JOIN users u ON c.contact_id = u.id
                        WHERE c.user_id = @UserId";
                    using (var cmd = new MySqlCommand(sql, conn))
                    {
                        cmd.Parameters.AddWithValue("@UserId", userId);
                        using (var reader = cmd.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                contacts.Add(new User
                                {
                                    Id = reader.GetInt32("id"),
                                    Email = reader.GetString("email"),
                                    DisplayName = reader.GetString("display_name"),
                                    Status = reader.IsDBNull(reader.GetOrdinal("status")) ? "Offline" : reader.GetString("status"),
                                    CustomSign = reader.IsDBNull(reader.GetOrdinal("custom_sign")) ? "" : reader.GetString("custom_sign"),
                                    AvatarFilename = reader.IsDBNull(reader.GetOrdinal("avatar_filename")) ? "" : reader.GetString("avatar_filename"),
                                    RelationType = reader.GetString("relation_type")
                                });
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[DB Error] GetContacts failed: {ex.Message}");
            }
            return contacts;
        }

        public User? GetUserById(int userId)
        {
            try
            {
                using (var conn = new MySqlConnection(_connectionString))
                {
                    conn.Open();
                    string sql = "SELECT id, email, display_name, status, custom_sign, avatar_filename FROM users WHERE id = @Id";
                    using (var cmd = new MySqlCommand(sql, conn))
                    {
                        cmd.Parameters.AddWithValue("@Id", userId);
                        using (var reader = cmd.ExecuteReader())
                        {
                            if (reader.Read())
                            {
                                return new User
                                {
                                    Id = reader.GetInt32("id"),
                                    Email = reader.GetString("email"),
                                    DisplayName = reader.GetString("display_name"),
                                    Status = reader.IsDBNull(reader.GetOrdinal("status")) ? "Offline" : reader.GetString("status"),
                                    CustomSign = reader.IsDBNull(reader.GetOrdinal("custom_sign")) ? "" : reader.GetString("custom_sign"),
                                    AvatarFilename = reader.IsDBNull(reader.GetOrdinal("avatar_filename")) ? "" : reader.GetString("avatar_filename")
                                };
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[DB Error] GetUserById failed: {ex.Message}");
            }
            return null;
        }
    }
}
