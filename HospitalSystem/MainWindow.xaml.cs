using System.Collections.ObjectModel;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using HospitalShared;

namespace HospitalSystem
{
    public partial class MainWindow : Window
    {
        private const string ServerHost = "127.0.0.1";
        private const int ServerPort = 5000;

        public ObservableCollection<Patient> Patients { get; } = new();

        public MainWindow()
        {
            InitializeComponent();
            LoadSamplePatients();
            PatientsDataGrid.ItemsSource = Patients;
        }

        private void LoadSamplePatients()
        {
            Patients.Add(new Patient
            {
                Id = 1,
                FullName = "Іваненко Олександр Петрович",
                Age = 42,
                Diagnosis = "Гіпертонічна хвороба",
                Doctor = "д-р Лисенко",
                Room = "204"
            });

            Patients.Add(new Patient
            {
                Id = 2,
                FullName = "Скрипник Марія Володимирівна",
                Age = 31,
                Diagnosis = "Пневмонія",
                Doctor = "д-р Шевченко",
                Room = "118"
            });

            Patients.Add(new Patient
            {
                Id = 3,
                FullName = "Грищенко Віталій Олегович",
                Age = 57,
                Diagnosis = "Артрит",
                Doctor = "д-р Кравчук",
                Room = "305"
            });
        }

        private void AddPatientButton_Click(object sender, RoutedEventArgs e)
        {
            var newPatient = new Patient { Id = GetNextPatientId() };
            var patient = OpenPatientEditor(newPatient, "Додати пацієнта");

            if (patient != null)
            {
                Patients.Add(patient);
            }
        }

        private void EditPatientButton_Click(object sender, RoutedEventArgs e)
        {
            if (PatientsDataGrid.SelectedItem is not Patient selectedPatient)
            {
                MessageBox.Show("Спочатку виберіть пацієнта для редагування.", "Увага", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var editablePatient = new Patient
            {
                Id = selectedPatient.Id,
                FullName = selectedPatient.FullName,
                Age = selectedPatient.Age,
                Diagnosis = selectedPatient.Diagnosis,
                Doctor = selectedPatient.Doctor,
                Room = selectedPatient.Room
            };

            var updatedPatient = OpenPatientEditor(editablePatient, "Редагувати пацієнта");

            if (updatedPatient != null)
            {
                var index = Patients.IndexOf(selectedPatient);
                Patients[index] = updatedPatient;
            }
        }

        private void DischargePatientButton_Click(object sender, RoutedEventArgs e)
        {
            if (PatientsDataGrid.SelectedItem is not Patient selectedPatient)
            {
                MessageBox.Show("Спочатку виберіть пацієнта для виписки.", "Увага", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var result = MessageBox.Show(
                $"Ви дійсно хочете виписати пацієнта {selectedPatient.FullName}?",
                "Підтвердження виписки",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                Patients.Remove(selectedPatient);
            }
        }

        private async void LoadFromServerButton_Click(object sender, RoutedEventArgs e)
        {
            var response = await SendRequestAsync("GET_ALL");
            if (!response.Success)
            {
                MessageBox.Show(response.Message, "Помилка сервера", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            Patients.Clear();
            foreach (var patient in response.Patients)
            {
                Patients.Add(patient);
            }
        }

        private async void SyncWithServerButton_Click(object sender, RoutedEventArgs e)
        {
            var response = await SendRequestAsync("SYNC", patients: Patients.ToList());
            if (!response.Success)
            {
                MessageBox.Show(response.Message, "Помилка синхронізації", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            MessageBox.Show("Синхронізація з сервером виконана успішно.", "Успішно", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private async Task<ApiResponse> SendRequestAsync(string action, Patient? patient = null, List<Patient>? patients = null)
        {
            try
            {
                var request = new PatientRequest
                {
                    Action = action,
                    Patient = patient,
                    Patients = patients ?? new List<Patient>()
                };

                using var client = new TcpClient();
                await client.ConnectAsync(ServerHost, ServerPort);
                using var stream = client.GetStream();

                var payload = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(request));
                var lengthBytes = BitConverter.GetBytes(payload.Length);

                await stream.WriteAsync(lengthBytes, 0, lengthBytes.Length);
                await stream.WriteAsync(payload, 0, payload.Length);

                var responseLengthBytes = new byte[4];
                await ReadExactlyAsync(stream, responseLengthBytes, 4);

                var responseLength = BitConverter.ToInt32(responseLengthBytes, 0);
                var responseBytes = new byte[responseLength];
                await ReadExactlyAsync(stream, responseBytes, responseLength);

                var json = Encoding.UTF8.GetString(responseBytes);
                return JsonSerializer.Deserialize<ApiResponse>(json) ?? new ApiResponse
                {
                    Success = false,
                    Message = "Server response could not be parsed."
                };
            }
            catch (Exception ex)
            {
                return new ApiResponse
                {
                    Success = false,
                    Message = $"Не вдалося з’єднатися з сервером: {ex.Message}"
                };
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
                    throw new InvalidOperationException("Connection closed while reading response.");
                }

                totalRead += read;
            }
        }

        private int GetNextPatientId()
        {
            var nextId = 1;

            foreach (var patient in Patients)
            {
                if (patient.Id >= nextId)
                {
                    nextId = patient.Id + 1;
                }
            }

            return nextId;
        }

        private Patient? OpenPatientEditor(Patient patient, string title)
        {
            var editor = new PatientEditorWindow(patient, title)
            {
                Owner = this
            };

            if (editor.ShowDialog() == true)
            {
                return editor.Patient;
            }

            return null;
        }

        private sealed class PatientEditorWindow : Window
        {
            private readonly TextBox _fullNameTextBox = new();
            private readonly TextBox _ageTextBox = new();
            private readonly TextBox _diagnosisTextBox = new();
            private readonly TextBox _doctorTextBox = new();
            private readonly TextBox _roomTextBox = new();

            public Patient Patient { get; }

            public PatientEditorWindow(Patient patient, string title)
            {
                Patient = patient;
                Title = title;
                Width = 420;
                Height = 360;
                ResizeMode = ResizeMode.NoResize;
                WindowStartupLocation = WindowStartupLocation.CenterOwner;

                var rootGrid = new Grid
                {
                    Margin = new Thickness(12)
                };

                rootGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                rootGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                rootGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                rootGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                rootGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                rootGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                rootGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

                rootGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                rootGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                AddField(rootGrid, 0, "ID:", new TextBox
                {
                    Width = 240,
                    IsReadOnly = true,
                    Text = patient.Id.ToString(),
                    Margin = new Thickness(0, 0, 0, 8)
                });

                AddField(rootGrid, 1, "ПІБ:", CreateInputField(patient.FullName, _fullNameTextBox));
                AddField(rootGrid, 2, "Вік:", CreateInputField(patient.Age.ToString(), _ageTextBox));
                AddField(rootGrid, 3, "Діагноз:", CreateInputField(patient.Diagnosis, _diagnosisTextBox));
                AddField(rootGrid, 4, "Лікар:", CreateInputField(patient.Doctor, _doctorTextBox));
                AddField(rootGrid, 5, "Кімната:", CreateInputField(patient.Room, _roomTextBox));

                var buttonsStack = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Margin = new Thickness(0, 18, 0, 0)
                };

                var saveButton = new Button
                {
                    Content = "Зберегти",
                    Width = 110,
                    Margin = new Thickness(0, 0, 10, 0),
                    IsDefault = true
                };
                saveButton.Click += SaveButton_Click;

                var cancelButton = new Button
                {
                    Content = "Скасувати",
                    Width = 110,
                    IsCancel = true
                };
                cancelButton.Click += (_, _) => Close();

                buttonsStack.Children.Add(saveButton);
                buttonsStack.Children.Add(cancelButton);

                Grid.SetColumnSpan(buttonsStack, 2);
                rootGrid.Children.Add(buttonsStack);
                Grid.SetRow(buttonsStack, 6);

                Content = rootGrid;
            }

            private static TextBox CreateInputField(string value, TextBox textBox)
            {
                textBox.Text = value;
                textBox.Width = 240;
                textBox.Margin = new Thickness(0, 0, 0, 8);
                return textBox;
            }

            private static void AddField(Grid rootGrid, int rowIndex, string labelText, Control inputControl)
            {
                var label = new Label
                {
                    Content = labelText,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 10, 8)
                };

                Grid.SetRow(label, rowIndex);
                Grid.SetColumn(label, 0);
                rootGrid.Children.Add(label);

                Grid.SetRow(inputControl, rowIndex);
                Grid.SetColumn(inputControl, 1);
                rootGrid.Children.Add(inputControl);
            }

            private void SaveButton_Click(object sender, RoutedEventArgs e)
            {
                if (string.IsNullOrWhiteSpace(_fullNameTextBox.Text))
                {
                    MessageBox.Show("Поле «ПІБ» не може бути порожнім.", "Помилка", MessageBoxButton.OK, MessageBoxImage.Warning);
                    _fullNameTextBox.Focus();
                    return;
                }

                if (!int.TryParse(_ageTextBox.Text, out var age) || age <= 0 || age > 150)
                {
                    MessageBox.Show("Введіть коректний вік пацієнта.", "Помилка", MessageBoxButton.OK, MessageBoxImage.Warning);
                    _ageTextBox.Focus();
                    return;
                }

                if (string.IsNullOrWhiteSpace(_diagnosisTextBox.Text))
                {
                    MessageBox.Show("Поле «Діагноз» не може бути порожнім.", "Помилка", MessageBoxButton.OK, MessageBoxImage.Warning);
                    _diagnosisTextBox.Focus();
                    return;
                }

                if (string.IsNullOrWhiteSpace(_doctorTextBox.Text))
                {
                    MessageBox.Show("Поле «Лікар» не може бути порожнім.", "Помилка", MessageBoxButton.OK, MessageBoxImage.Warning);
                    _doctorTextBox.Focus();
                    return;
                }

                if (string.IsNullOrWhiteSpace(_roomTextBox.Text))
                {
                    MessageBox.Show("Поле «Кімната» не може бути порожнім.", "Помилка", MessageBoxButton.OK, MessageBoxImage.Warning);
                    _roomTextBox.Focus();
                    return;
                }

                Patient.FullName = _fullNameTextBox.Text.Trim();
                Patient.Age = age;
                Patient.Diagnosis = _diagnosisTextBox.Text.Trim();
                Patient.Doctor = _doctorTextBox.Text.Trim();
                Patient.Room = _roomTextBox.Text.Trim();

                DialogResult = true;
                Close();
            }
        }
    }
}
