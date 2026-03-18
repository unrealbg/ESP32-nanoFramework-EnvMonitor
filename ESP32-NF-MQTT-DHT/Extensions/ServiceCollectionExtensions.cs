namespace ESP32_NF_MQTT_DHT.Extensions
{
    using ESP32_NF_MQTT_DHT.Configuration;
    using ESP32_NF_MQTT_DHT.Helpers;
    using ESP32_NF_MQTT_DHT.Managers;
    using ESP32_NF_MQTT_DHT.Managers.Contracts;
    using ESP32_NF_MQTT_DHT.Modules;
    using ESP32_NF_MQTT_DHT.Modules.Contracts;
    using ESP32_NF_MQTT_DHT.Services;
    using ESP32_NF_MQTT_DHT.Services.Contracts;
    using ESP32_NF_MQTT_DHT.Services.NoOp;
    using ESP32_NF_MQTT_DHT.Services.MQTT;
    using ESP32_NF_MQTT_DHT.Services.MQTT.Contracts;

    using Microsoft.Extensions.DependencyInjection;

    /// <summary>
    /// Extension methods for the <see cref="IServiceCollection"/> interface.
    /// </summary>
    public static class ServiceCollectionExtensions
    {
        public static IServiceCollection AddSensorServices(this IServiceCollection services, SensorType sensorType)
        {
            services.AddSingleton(typeof(ISensorService), SensorServiceFactory.GetSensorServiceType(sensorType));
            return services;
        }

        public static IServiceCollection AddCoreServices(this IServiceCollection services)
        {
            // Platform and infrastructure services
            services.AddSingleton(typeof(IPlatformService), typeof(PlatformService));
            services.AddSingleton(typeof(IServiceStartupManager), typeof(ServiceStartupManager));
            
            // Connection services
            services.AddSingleton(typeof(IConnectionService), typeof(ConnectionService));
            services.AddSingleton(typeof(IInternetConnectionService), typeof(InternetConnectionService));
            
            // Communication services
            services.AddSingleton(typeof(IMqttClientService), AppConfiguration.Features.EnableMqttClient ? typeof(MqttClientService) : typeof(NoOpMqttClientService));
            services.AddSingleton(typeof(IMqttConnectionManager), typeof(MqttConnectionManager));
            services.AddSingleton(typeof(IMqttPublishService), typeof(MqttPublishService));
            services.AddSingleton(typeof(IIrcBotService), AppConfiguration.Features.EnableIrcBot ? typeof(IrcBotService) : typeof(NoOpIrcBotService));
            services.AddSingleton(typeof(ITcpListenerService), AppConfiguration.Features.EnableTcpConsole ? typeof(TcpListenerService) : typeof(NoOpTcpListenerService));
            services.AddSingleton(typeof(IWebServerService), AppConfiguration.Features.EnableWebServer ? typeof(WebServerService) : typeof(NoOpWebServerService));
            services.AddSingleton(typeof(IOtaService), AppConfiguration.Features.EnableMqttClient && AppConfiguration.Features.EnableOtaOverMqtt ? typeof(OtaService) : typeof(NoOpOtaService));
            
            // Hardware services
            services.AddSingleton(typeof(IRelayService), typeof(RelayService));
            services.AddSingleton(typeof(ISensorManager), typeof(SensorManager));
            
            // Utility services
            services.AddSingleton(typeof(IUptimeService), typeof(UptimeService));
            services.AddSingleton(typeof(MqttMessageHandler));

            // Modules
            services.AddSingleton(typeof(IModuleManager), typeof(ModuleManager));
            services.AddSingleton(typeof(OtaModule));

            return services;
        }
    }
}
