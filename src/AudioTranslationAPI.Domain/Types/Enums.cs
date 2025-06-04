namespace AudioTranslationAPI.Domain.Types
{
    /// <summary>
    /// Estados posibles de una traducción de audio
    /// </summary>
    public enum TranslationStatus
    {
        /// <summary>
        /// Traducción creada y en cola para procesamiento
        /// </summary>
        Queued = 0,

        /// <summary>
        /// Validando archivo de audio
        /// </summary>
        Validating = 1,

        /// <summary>
        /// Convirtiendo audio a texto (Speech-to-Text)
        /// </summary>
        ProcessingSpeechToText = 2,

        /// <summary>
        /// Traduciendo texto del idioma origen al destino
        /// </summary>
        ProcessingTranslation = 3,

        /// <summary>
        /// Convirtiendo texto traducido a audio (Text-to-Speech)
        /// </summary>
        ProcessingTextToSpeech = 4,

        /// <summary>
        /// Traducción completada exitosamente
        /// </summary>
        Completed = 5,

        /// <summary>
        /// Error durante el procesamiento
        /// </summary>
        Failed = 6,

        /// <summary>
        /// Traducción cancelada por el usuario
        /// </summary>
        Cancelled = 7,

        /// <summary>
        /// Traducción expirada (no descargada a tiempo)
        /// </summary>
        Expired = 8
    }

    /// <summary>
    /// Formatos de audio soportados
    /// </summary>
    public enum AudioFormat
    {
        /// <summary>
        /// Formato WAV sin compresión
        /// </summary>
        Wav = 0,

        /// <summary>
        /// Formato MP3 comprimido
        /// </summary>
        Mp3 = 1,

        /// <summary>
        /// Formato OGG Vorbis
        /// </summary>
        Ogg = 2,

        /// <summary>
        /// Formato AAC
        /// </summary>
        Aac = 3,

        /// <summary>
        /// Formato FLAC sin pérdida
        /// </summary>
        Flac = 4
    }

    /// <summary>
    /// Idiomas soportados para traducción
    /// </summary>
    public enum SupportedLanguage
    {
        /// <summary>
        /// Español
        /// </summary>
        Spanish = 0,

        /// <summary>
        /// Inglés
        /// </summary>
        English = 1,

        /// <summary>
        /// Francés (futuro)
        /// </summary>
        French = 2,

        /// <summary>
        /// Portugués (futuro)
        /// </summary>
        Portuguese = 3,

        /// <summary>
        /// Italiano (futuro)
        /// </summary>
        Italian = 4,

        /// <summary>
        /// Alemán (futuro)
        /// </summary>
        German = 5
    }

    /// <summary>
    /// Prioridad de procesamiento
    /// </summary>
    public enum ProcessingPriority
    {
        /// <summary>
        /// Prioridad baja - usuarios gratuitos
        /// </summary>
        Low = 0,

        /// <summary>
        /// Prioridad normal - usuarios estándar
        /// </summary>
        Normal = 1,

        /// <summary>
        /// Prioridad alta - usuarios premium
        /// </summary>
        High = 2,

        /// <summary>
        /// Prioridad crítica - uso interno/administrativo
        /// </summary>
        Critical = 3
    }

    /// <summary>
    /// Calidad de salida del audio
    /// </summary>
    public enum AudioQuality
    {
        /// <summary>
        /// Baja calidad - 8kHz, más rápido
        /// </summary>
        Low = 0,

        /// <summary>
        /// Calidad estándar - 16kHz
        /// </summary>
        Standard = 1,

        /// <summary>
        /// Alta calidad - 22kHz
        /// </summary>
        High = 2,

        /// <summary>
        /// Máxima calidad - 44kHz
        /// </summary>
        Premium = 3
    }
}
