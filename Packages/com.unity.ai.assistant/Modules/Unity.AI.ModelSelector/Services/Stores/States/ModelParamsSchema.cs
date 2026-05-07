using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using UnityEngine;

namespace Unity.AI.ModelSelector.Services.Stores.States
{
    /// <summary>
    /// Strongly typed representation of the JSON Schema used in ModelFullResult.ParamsSchema.
    /// The raw JSON is stored in a Unity-serializable field so that the parsed dictionaries
    /// survive domain reloads (Unity cannot serialize Dictionary or auto-properties).
    /// </summary>
    [Serializable]
    class ModelParamsSchema
    {
        public const string DimensionsProperty = "dimensions";
        public const string WidthProperty = "width";
        public const string HeightProperty = "height";
        public const string AspectRatioProperty = "aspect_ratio";

        [SerializeField]
        string m_RawJson;

        [NonSerialized]
        Dictionary<string, SchemaProperty> m_Properties;

        [NonSerialized]
        List<string> m_Required;

        [NonSerialized]
        List<List<string>> m_AnyOf;

        [NonSerialized]
        bool m_AdditionalProperties;

        [NonSerialized]
        bool m_Parsed;

        [JsonProperty("properties")]
        public Dictionary<string, SchemaProperty> Properties
        {
            get
            {
                EnsureParsed();
                return m_Properties;
            }
            set => m_Properties = value;
        }

        [JsonProperty("required")]
        public List<string> Required
        {
            get
            {
                EnsureParsed();
                return m_Required;
            }
            set => m_Required = value;
        }

        /// <summary>
        /// Structured representation of JSON Schema anyOf branches.
        /// Each inner list contains the required keys for one branch.
        /// At least one branch must be fully satisfied.
        /// </summary>
        [JsonProperty("anyOf")]
        public List<List<string>> AnyOf
        {
            get
            {
                EnsureParsed();
                return m_AnyOf;
            }
            set => m_AnyOf = value;
        }

        [JsonProperty("additionalProperties")]
        public bool AdditionalProperties
        {
            get
            {
                EnsureParsed();
                return m_AdditionalProperties;
            }
            set => m_AdditionalProperties = value;
        }

        public string RawJson
        {
            get => m_RawJson;
            set => m_RawJson = value;
        }

        void EnsureParsed()
        {
            if (m_Parsed || m_Properties != null || string.IsNullOrEmpty(m_RawJson))
                return;

            try
            {
                var rawSchema = Newtonsoft.Json.Linq.JObject.Parse(m_RawJson);

                if (rawSchema["required"] != null)
                    m_Required = rawSchema["required"].ToObject<List<string>>();

                var anyOfToken = rawSchema["anyOf"];
                if (anyOfToken is Newtonsoft.Json.Linq.JArray anyOfArray)
                {
                    m_AnyOf = new List<List<string>>();
                    foreach (var item in anyOfArray)
                    {
                        if (item is Newtonsoft.Json.Linq.JObject obj &&
                            obj["required"] is Newtonsoft.Json.Linq.JArray reqArray)
                        {
                            var branch = reqArray.Select(k => k?.ToString())
                                .Where(k => !string.IsNullOrEmpty(k)).ToList();
                            if (branch.Count > 0)
                                m_AnyOf.Add(branch);
                        }
                    }
                }

                if (m_AnyOf == null || m_AnyOf.Count == 0)
                {
                    var oneOfToken = rawSchema["oneOf"];
                    if (oneOfToken is Newtonsoft.Json.Linq.JArray oneOfArray)
                    {
                        m_AnyOf = new List<List<string>>();
                        foreach (var item in oneOfArray)
                        {
                            if (item is Newtonsoft.Json.Linq.JObject obj &&
                                obj["required"] is Newtonsoft.Json.Linq.JArray reqArray)
                            {
                                var branch = reqArray.Select(k => k?.ToString())
                                    .Where(k => !string.IsNullOrEmpty(k)).ToList();
                                if (branch.Count > 0)
                                    m_AnyOf.Add(branch);
                            }
                        }
                    }
                }

                if (rawSchema["additionalProperties"] != null)
                    m_AdditionalProperties = rawSchema["additionalProperties"].ToObject<bool>();

                var propertiesToken = rawSchema["properties"];
                if (propertiesToken is Newtonsoft.Json.Linq.JObject propertiesObj)
                {
                    m_Properties = new Dictionary<string, SchemaProperty>();
                    foreach (var prop in propertiesObj.Properties())
                    {
                        var schemaProp = prop.Value.ToObject<SchemaProperty>();
                        m_Properties[prop.Name] = schemaProp;
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"Failed to re-parse ModelParamsSchema from raw JSON: {e.Message}");
            }
            finally
            {
                m_Parsed = true;
            }
        }

        /// <summary>
        /// Returns true if the key is unconditionally required (in top-level Required),
        /// or if every anyOf branch requires it.
        /// </summary>
        public bool IsRequired(string key)
        {
            if (Required?.Contains(key) == true)
                return true;
            if (AnyOf is { Count: > 0 } && AnyOf.All(branch => branch.Contains(key)))
                return true;
            return false;
        }

        /// <summary>
        /// Returns true if at least one anyOf branch is fully satisfied by the provided keys.
        /// Returns true when there are no anyOf branches.
        /// </summary>
        public bool IsAnyOfSatisfied(ISet<string> providedKeys)
        {
            if (AnyOf == null || AnyOf.Count == 0)
                return true;
            return AnyOf.Any(branch => branch.All(key => providedKeys.Contains(key)));
        }
    }

    [Serializable]
    class SchemaProperty
    {
        [JsonProperty("type")]
        public object Type { get; set; }

        [JsonProperty("description")]
        public string Description { get; set; }

        [JsonProperty("enum")]
        public List<object> Enum { get; set; }

        [JsonProperty("default")]
        public object Default { get; set; }

        [JsonProperty("minLength")]
        public int? MinLength { get; set; }

        [JsonProperty("maxLength")]
        public int? MaxLength { get; set; }

        [JsonProperty("minimum")]
        public double? Minimum { get; set; }

        [JsonProperty("maximum")]
        public double? Maximum { get; set; }

        [JsonProperty("maxItems")]
        public int? MaxItems { get; set; }

        [JsonProperty("minItems")]
        public int? MinItems { get; set; }

        [JsonProperty("items")]
        public SchemaProperty Items { get; set; }

        [JsonProperty("x-semantic-type")]
        public string SemanticType { get; set; }

        [JsonProperty("properties")]
        public Dictionary<string, SchemaProperty> Properties { get; set; }

        [JsonProperty("required")]
        public List<string> Required { get; set; }
    }
}
