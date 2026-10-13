namespace HospitalShared;

public class Patient
{
    public int Id { get; set; }
    public string FullName { get; set; } = string.Empty;
    public int Age { get; set; }
    public string Diagnosis { get; set; } = string.Empty;
    public string Doctor { get; set; } = string.Empty;
    public string Room { get; set; } = string.Empty;
}

public class PatientRequest
{
    public string Action { get; set; } = string.Empty;
    public Patient? Patient { get; set; }
    public List<Patient> Patients { get; set; } = new();
}

public class ApiResponse
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public List<Patient> Patients { get; set; } = new();
}
