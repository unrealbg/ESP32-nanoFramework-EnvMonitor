namespace ESP32_NF_MQTT_DHT.Modules.Contracts
{
    /// <summary>
    /// Manages lifecycle of registered modules.
    /// </summary>
    public interface IModuleManager
    {
        /// <summary>
        /// Registers a module instance for lifecycle management.
        /// </summary>
        /// <param name="module">Module to register.</param>
        void Register(IModule module);

        /// <summary>
        /// Discovers and registers modules from a directory.
        /// Implementations may ignore this call when dynamic loading is not supported.
        /// </summary>
        /// <param name="dir">Directory containing module files.</param>
        /// <returns>Number of modules loaded and registered.</returns>
        int LoadFromDirectory(string dir);

        /// <summary>
        /// Starts all registered modules.
        /// </summary>
        void StartAll();

        /// <summary>
        /// Stops all registered modules.
        /// </summary>
        void StopAll();
    }
}
