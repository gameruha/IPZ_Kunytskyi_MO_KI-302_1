using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using HospitalShared;
using Microsoft.Data.Sqlite;

var server = new HospitalTcpServer();
await server.StartAsync();

public sealed class HospitalTcpServer
{
    private const int Port = 5000;
    private readonly PatientRepository _repository = new();

    public async Task StartAsync()
    {
        var listener = new TcpListener(IPAddress.Loopback, Port);
        listener.Start();

        Console.WriteLine($"Hospital server started on {IPAddress.Loopback}:{Port}");

        while (true)
        {
            var client = await listener.AcceptTcpClientAsync();
            _ = Task.Run(() => HandleClientAsync(client));
        }
    }

    private async Task HandleClientAsync(TcpClient client)
    {
        try
        {
            using var stream = client.GetStream();
            var lengthBuffer = new byte[4];
            await ReadExactlyAsync(stream, lengthBuffer, 4);

            var messageLength = BitConverter.ToInt32(lengthBuffer, 0);
            var payload = new byte[messageLength];
            await ReadExactlyAsync(stream, payload, messageLength);

            var json = Encoding.UTF8.GetString(payload);
            var request = JsonSerializer.Deserialize<PatientRequest>(json) ?? new PatientRequest();

            ApiResponse response;

            switch (request.Action)
            {
                case "GET_ALL":
                    response = new ApiResponse
                    {
                        Success = true,
                        Message = "Patients loaded successfully.",
                        Patients = _repository.GetAll()
                    };
                    break;

                case "ADD":
                    if (request.Patient is null)
                    {
                        response = new ApiResponse { Success = false, Message = "Patient payload is required." };
                        break;
                    }

                    response = new ApiResponse
                    {
                        Success = true,
                        Message = "Patient added successfully.",
                        Patients = _repository.Add(request.Patient)
                    };
                    break;

                case "UPDATE":
                    if (request.Patient is null)
                    {
                        response = new ApiResponse { Success = false, Message = "Patient payload is required." };
                        break;
                    }

                    var updated = _repository.Update(request.Patient);
                    response = new ApiResponse
                    {
                        Success = updated,
                        Message = updated ? "Patient updated successfully." : "Patient not found.",
                        Patients = _repository.GetAll()
                    };
                    break;

                case "DELETE":
                    if (request.Patient is null)
                    {
                        response = new ApiResponse { Success = false, Message = "Patient payload is required." };
                        break;
                    }

                    var removed = _repository.Delete(request.Patient.Id);
                    response = new ApiResponse
                    {
                        Success = removed,
                        Message = removed ? "Patient discharged successfully." : "Patient not found.",
                        Patients = _repository.GetAll()
                    };
                    break;

                case "SYNC":
                    var syncedPatients = request.Patients ?? new List<Patient>();
                    _repository.ReplaceAll(syncedPatients);
                    response = new ApiResponse
                    {
                        Success = true,
                        Message = "Patients synced successfully.",
                        Patients = _repository.GetAll()
                    };
                    break;

                default:
                    response = new ApiResponse
                    {
                        Success = false,
                        Message = $"Unsupported action: {request.Action}"
                    };
                    break;
            }

            var payloadJson = JsonSerializer.Serialize(response);
            var payloadBytes = Encoding.UTF8.GetBytes(payloadJson);
            var header = BitConverter.GetBytes(payloadBytes.Length);

            await stream.WriteAsync(header, 0, header.Length);
            await stream.WriteAsync(payloadBytes, 0, payloadBytes.Length);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Exception: {ex.Message}");
            try
            {
                var errorResponse = new ApiResponse
                {
                    Success = false,
                    Message = ex.Message
                };

                var json = JsonSerializer.Serialize(errorResponse);
                var bytes = Encoding.UTF8.GetBytes(json);
                var header = BitConverter.GetBytes(bytes.Length);

                using var stream = client.GetStream();
                await stream.WriteAsync(header, 0, header.Length);
                await stream.WriteAsync(bytes, 0, bytes.Length);
            }
            catch
            {
                // ignored
            }
        }
        finally
        {
            client.Close();
        }
    }

    private static async Task ReadExactlyAsync(NetworkStream stream, byte[] buffer, int length)
    {
        var totalRead = 0;

        while (totalRead < length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(totalRead, length - totalRead));
            if (read == 0)
            {
                throw new InvalidOperationException("Connection closed while reading data.");
            }

            totalRead += read;
        }
    }
}

public sealed class PatientRepository
{
    private readonly string _databasePath = Path.Combine(AppContext.BaseDirectory, "hospital.db");

    public PatientRepository()
    {
        Initialize();
    }

    private void Initialize()
    {
        using var connection = new SqliteConnection($"Data Source={_databasePath}");
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = @"
            CREATE TABLE IF NOT EXISTS Patients (
                Id INTEGER PRIMARY KEY,
                FullName TEXT NOT NULL,
                Age INTEGER NOT NULL,
                Diagnosis TEXT NOT NULL,
                Doctor TEXT NOT NULL,
                Room TEXT NOT NULL
            );";

        command.ExecuteNonQuery();
    }

    public List<Patient> GetAll()
    {
        var result = new List<Patient>();

        using var connection = new SqliteConnection($"Data Source={_databasePath}");
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT Id, FullName, Age, Diagnosis, Doctor, Room FROM Patients ORDER BY Id;";

        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            result.Add(new Patient
            {
                Id = reader.GetInt32(0),
                FullName = reader.GetString(1),
                Age = reader.GetInt32(2),
                Diagnosis = reader.GetString(3),
                Doctor = reader.GetString(4),
                Room = reader.GetString(5)
            });
        }

        return result;
    }

    public List<Patient> Add(Patient patient)
    {
        var nextId = GetNextId();
        patient.Id = nextId;

        using var connection = new SqliteConnection($"Data Source={_databasePath}");
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = @"
            INSERT INTO Patients (Id, FullName, Age, Diagnosis, Doctor, Room)
            VALUES (@id, @fullName, @age, @diagnosis, @doctor, @room);";

        command.Parameters.AddWithValue("@id", patient.Id);
        command.Parameters.AddWithValue("@fullName", patient.FullName);
        command.Parameters.AddWithValue("@age", patient.Age);
        command.Parameters.AddWithValue("@diagnosis", patient.Diagnosis);
        command.Parameters.AddWithValue("@doctor", patient.Doctor);
        command.Parameters.AddWithValue("@room", patient.Room);

        command.ExecuteNonQuery();

        return GetAll();
    }

    public bool Update(Patient patient)
    {
        using var connection = new SqliteConnection($"Data Source={_databasePath}");
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = @"
            UPDATE Patients
            SET FullName = @fullName,
                Age = @age,
                Diagnosis = @diagnosis,
                Doctor = @doctor,
                Room = @room
            WHERE Id = @id;";

        command.Parameters.AddWithValue("@id", patient.Id);
        command.Parameters.AddWithValue("@fullName", patient.FullName);
        command.Parameters.AddWithValue("@age", patient.Age);
        command.Parameters.AddWithValue("@diagnosis", patient.Diagnosis);
        command.Parameters.AddWithValue("@doctor", patient.Doctor);
        command.Parameters.AddWithValue("@room", patient.Room);

        return command.ExecuteNonQuery() > 0;
    }

    public bool Delete(int id)
    {
        using var connection = new SqliteConnection($"Data Source={_databasePath}");
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM Patients WHERE Id = @id;";
        command.Parameters.AddWithValue("@id", id);

        return command.ExecuteNonQuery() > 0;
    }

    public void ReplaceAll(IEnumerable<Patient> patients)
    {
        using var connection = new SqliteConnection($"Data Source={_databasePath}");
        connection.Open();

        using (var command = connection.CreateCommand())
        {
            command.CommandText = "DELETE FROM Patients;";
            command.ExecuteNonQuery();
        }

        foreach (var patient in patients)
        {
            patient.Id = GetNextId();
            using var insertCommand = connection.CreateCommand();
            insertCommand.CommandText = @"
                INSERT INTO Patients (Id, FullName, Age, Diagnosis, Doctor, Room)
                VALUES (@id, @fullName, @age, @diagnosis, @doctor, @room);";

            insertCommand.Parameters.AddWithValue("@id", patient.Id);
            insertCommand.Parameters.AddWithValue("@fullName", patient.FullName);
            insertCommand.Parameters.AddWithValue("@age", patient.Age);
            insertCommand.Parameters.AddWithValue("@diagnosis", patient.Diagnosis);
            insertCommand.Parameters.AddWithValue("@doctor", patient.Doctor);
            insertCommand.Parameters.AddWithValue("@room", patient.Room);

            insertCommand.ExecuteNonQuery();
        }
    }

    private int GetNextId()
    {
        using var connection = new SqliteConnection($"Data Source={_databasePath}");
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COALESCE(MAX(Id), 0) + 1 FROM Patients;";

        var result = command.ExecuteScalar();
        return result is null ? 1 : Convert.ToInt32(result);
    }
}

