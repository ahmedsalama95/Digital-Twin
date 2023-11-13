using Microsoft.Azure.Devices.Client;
using Microsoft.Azure.Devices.Shared;
using Newtonsoft.Json;
using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Pump
{
    internal enum StatusCode
    {
        Completed = 200,
        InProgress = 202,
        ReportDeviceInitialProperty = 203,
        BadRequest = 400,
        NotFound = 404
    }
    public class Pump
    {
        private readonly Random _random = new();

        private readonly DeviceClient _deviceClient;

        

        //Variables default values
        private double _OptimalValue = 3;
        
        private string _Working = "Working";
        private bool _maintenanceState = false;
        
        
        

        public Pump(DeviceClient deviceClient)
        {
            _deviceClient = deviceClient ?? throw new ArgumentNullException(nameof(deviceClient));

            //When device starts it randomly sets the warranty state to either true or false.
            
        }

        //<Workflow>
        public async Task PerformOperationsAsync(CancellationToken cancellationToken)
        {
            Console.WriteLine($"Device successfully connected to Azure IoT Central");
            
            Console.WriteLine($"- Set handler for \"SetMaintenanceMode\" command.");
            await _deviceClient.SetMethodHandlerAsync("SetMaintenanceMode", HandleMaintenanceModeCommand, _deviceClient, cancellationToken);

            

            Console.WriteLine($"- Set handler to receive \"OptimalValue\" updates.");
            await _deviceClient.SetDesiredPropertyUpdateCallbackAsync(OptimalValueUpdateCallbackAsync, _deviceClient, cancellationToken);

            

            while (!cancellationToken.IsCancellationRequested)
            {
                await SendTelemetryAsync(cancellationToken);
                await Task.Delay(70000, cancellationToken);
            }
        }
        //</Workflow>

        //<Telemetry>
        //Send temperature and humidity telemetry, whether it's currently brewing and when a cup is detected.
        private async Task SendTelemetryAsync(CancellationToken cancellationToken)
        {
            //Simulate the telemetry values
            double PumpVibration = _OptimalValue + (_random.NextDouble() * 2) - 2.2;
            

            

            // Create JSON message
            string messageBody = JsonConvert.SerializeObject(
                new
                {
                    PumpVibration = PumpVibration,
                    
                    Working = _Working
                });
            using var message = new Message(Encoding.ASCII.GetBytes(messageBody))
            {
                ContentType = "application/json",
                ContentEncoding = "utf-8",
            };

            //Show the information in console
            double infoPumpVibration = Math.Round(PumpVibration, 0);
            
            
            string infoWorking = _Working == "Working" ? "Y" : "N";
            string infoMaintenance = _maintenanceState ? "Y" : "N";

            Console.WriteLine($"Telemetry send: VibrationValue: {infoPumpVibration}mm/s " +
                $" Working State: {infoWorking} Maintenance Mode: {infoMaintenance}");

            //Send the message
            await _deviceClient.SendEventAsync(message, cancellationToken);
        }
        //</Telemetry>

        //<Commands>
        // The callback to handle "SetMaintenanceMode" command.
        private Task<MethodResponse> HandleMaintenanceModeCommand(MethodRequest request, object userContext)
        {
            try
            {
                Console.WriteLine(" * Maintenance command received");

                if (_maintenanceState)
                {
                    Console.WriteLine(" - Warning: The device is already in the maintenance mode.");
                }

                //Set state
                _maintenanceState = true;

                //Send response
                byte[] responsePayload = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject("Success"));
                return Task.FromResult(new MethodResponse(responsePayload, (int)StatusCode.Completed));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Exception while handling \"SetMaintenanceMode\" command: {ex}");
                return Task.FromResult(new MethodResponse((int)StatusCode.BadRequest));
            }

        }

        //</Commands>

        // The desired property update callback, which receives the OptimalValue as a desired property update,
        // and updates the current _optimalValue value over telemetry and reported property update.

        //<Properties>
        private async Task OptimalValueUpdateCallbackAsync(TwinCollection desiredProperties, object userContext)
        {
            const string propertyName = "OptimalValue";

            (bool optimalVibUpdateReceived, double optimalVib) = GetPropertyFromTwin<double>(desiredProperties, propertyName);
            if (optimalVibUpdateReceived)
            {
                Console.WriteLine($" * Property: Received - {{ \"{propertyName}\": {optimalVib}mm/s }}.");

                //Update reported property to In Progress
                string jsonPropertyPending = $"{{ \"{propertyName}\": {{ \"value\": {optimalVib}, \"ac\": {(int)StatusCode.InProgress}, " +
                    $"\"av\": {desiredProperties.Version}, \"ad\": \"In progress - reporting optimal temperature\" }} }}";
                var reportedPropertyPending = new TwinCollection(jsonPropertyPending);
                await _deviceClient.UpdateReportedPropertiesAsync(reportedPropertyPending);
                Console.WriteLine($" * Property: Update - {{\"{propertyName} \": {optimalVib}mm/s}} is {StatusCode.InProgress}.");

                //Update the optimal temperature
                _OptimalValue = optimalVib;

                //Update reported property to Completed
                string jsonProperty = $"{{ \"{propertyName}\": {{ \"value\": {optimalVib}, \"ac\": {(int)StatusCode.Completed}, " +
                    $"\"av\": {desiredProperties.Version}, \"ad\": \"Successfully updated optimal Vibration Value\" }} }}";
                var reportedProperty = new TwinCollection(jsonProperty);
                await _deviceClient.UpdateReportedPropertiesAsync(reportedProperty);
                Console.WriteLine($" * Property: Update - {{\"{propertyName} \": {optimalVib}mm/s }} is {StatusCode.Completed}.");
            }
            else
            {
                Console.WriteLine($" * Property: Received an unrecognized property update from service:\n{desiredProperties.ToJson()}");
            }
        }

        
        //</Properties>

        private static (bool, T) GetPropertyFromTwin<T>(TwinCollection collection, string propertyName)
        {
            return collection.Contains(propertyName) ? (true, (T)collection[propertyName]) : (false, default);
        }
    }
}
