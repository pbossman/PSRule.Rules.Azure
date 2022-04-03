﻿// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.IO;
using System.Management.Automation;
using Newtonsoft.Json;
using PSRule.Rules.Azure.Data.Policy;
using PSRule.Rules.Azure.Data.Template;
using PSRule.Rules.Azure.Pipeline;
using PSRule.Rules.Azure.Resources;

namespace PSRule.Rules.Azure
{
    /// <summary>
    /// A custom serializer to correctly convert PSObject properties to JSON instead of CLIXML.
    /// </summary>
    internal sealed class PSObjectJsonConverter : JsonConverter
    {
        public override bool CanConvert(Type objectType)
        {
            return objectType == typeof(PSObject);
        }

        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            if (!(value is PSObject obj))
                throw new ArgumentException(message: PSRuleResources.SerializeNullPSObject, paramName: nameof(value));

            if (value is FileSystemInfo fileSystemInfo)
            {
                WriteFileSystemInfo(writer, fileSystemInfo, serializer);
                return;
            }
            writer.WriteStartObject();
            foreach (var property in obj.Properties)
            {
                // Ignore properties that are not readable or can cause race condition
                if (!property.IsGettable || property.Value is PSDriveInfo || property.Value is ProviderInfo || property.Value is DirectoryInfo)
                    continue;

                writer.WritePropertyName(property.Name);
                serializer.Serialize(writer, property.Value);
            }
            writer.WriteEndObject();
        }

        public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
        {
            // Create target object based on JObject
            var result = existingValue as PSObject ?? new PSObject();

            // Read tokens
            ReadObject(value: result, reader: reader);
            return result;
        }

        private static void ReadObject(PSObject value, JsonReader reader)
        {
            if (reader.TokenType != JsonToken.StartObject)
                throw new PipelineSerializationException(PSRuleResources.ReadJsonFailed);

            reader.Read();
            string name = null;

            // Read each token
            while (reader.TokenType != JsonToken.EndObject)
            {
                switch (reader.TokenType)
                {
                    case JsonToken.PropertyName:
                        name = reader.Value.ToString();
                        break;

                    case JsonToken.StartObject:
                        var child = new PSObject();
                        ReadObject(value: child, reader: reader);
                        value.Properties.Add(new PSNoteProperty(name: name, value: child));
                        break;

                    case JsonToken.StartArray:
                        var items = new List<object>();
                        reader.Read();

                        while (reader.TokenType != JsonToken.EndArray)
                        {
                            items.Add(ReadValue(reader));
                            reader.Read();
                        }

                        value.Properties.Add(new PSNoteProperty(name: name, value: items.ToArray()));
                        break;

                    default:
                        value.Properties.Add(new PSNoteProperty(name: name, value: reader.Value));
                        break;
                }
                reader.Read();
            }
        }

        private static object ReadValue(JsonReader reader)
        {
            if (reader.TokenType != JsonToken.StartObject)
                return reader.Value;

            var value = new PSObject();
            ReadObject(value, reader);
            return value;
        }

        private static void WriteFileSystemInfo(JsonWriter writer, FileSystemInfo value, JsonSerializer serializer)
        {
            serializer.Serialize(writer, value.FullName);
        }
    }

    /// <summary>
    /// A custom serializer to convert PSObjects that may or maynot be in a JSON array to an a PSObject array.
    /// </summary>
    internal sealed class PSObjectArrayJsonConverter : JsonConverter
    {
        public override bool CanConvert(Type objectType)
        {
            return objectType == typeof(PSObject[]);
        }

        public override bool CanWrite => false;

        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            throw new NotImplementedException();
        }

        public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
        {
            if (reader.TokenType != JsonToken.StartObject && reader.TokenType != JsonToken.StartArray)
                throw new PipelineSerializationException(PSRuleResources.ReadJsonFailed);

            var result = new List<PSObject>();
            var isArray = reader.TokenType == JsonToken.StartArray;

            if (isArray)
                reader.Read();

            while (!isArray || (isArray && reader.TokenType != JsonToken.EndArray))
            {
                var value = ReadObject(reader: reader);
                result.Add(value);

                // Consume the EndObject token
                if (isArray)
                {
                    reader.Read();
                }
            }
            return result.ToArray();
        }

        private static PSObject ReadObject(JsonReader reader)
        {
            if (reader.TokenType != JsonToken.StartObject)
                throw new PipelineSerializationException(PSRuleResources.ReadJsonFailed);

            reader.Read();
            var result = new PSObject();
            string name = null;

            // Read each token
            while (reader.TokenType != JsonToken.EndObject)
            {
                switch (reader.TokenType)
                {
                    case JsonToken.PropertyName:
                        name = reader.Value.ToString();
                        break;

                    case JsonToken.StartObject:
                        var value = ReadObject(reader: reader);
                        result.Properties.Add(new PSNoteProperty(name: name, value: value));
                        break;

                    case JsonToken.StartArray:
                        var items = ReadArray(reader: reader);
                        result.Properties.Add(new PSNoteProperty(name: name, value: items));

                        break;

                    default:
                        result.Properties.Add(new PSNoteProperty(name: name, value: reader.Value));
                        break;
                }
                reader.Read();
            }
            return result;
        }

        private static PSObject[] ReadArray(JsonReader reader)
        {
            if (reader.TokenType != JsonToken.StartArray)
                throw new PipelineSerializationException(PSRuleResources.ReadJsonFailed);

            reader.Read();
            var result = new List<PSObject>();

            while (reader.TokenType != JsonToken.EndArray)
            {
                if (reader.TokenType == JsonToken.StartObject)
                {
                    result.Add(ReadObject(reader: reader));
                }
                else if (reader.TokenType == JsonToken.StartArray)
                {
                    result.Add(PSObject.AsPSObject(ReadArray(reader)));
                }
                else
                {
                    result.Add(PSObject.AsPSObject(reader.Value));
                }
                reader.Read();
            }
            return result.ToArray();
        }
    }

    internal sealed class PolicyAliasProviderConverter : JsonConverter
    {
        public override bool CanRead => true;

        public override bool CanWrite => false;

        public override bool CanConvert(Type objectType)
        {
            return objectType == typeof(PolicyAliasProvider);
        }

        public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
        {
            var aliasProvider = existingValue as PolicyAliasProvider ?? new PolicyAliasProvider();
            ReadAliasProvider(aliasProvider, reader);
            return aliasProvider;
        }

        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            throw new NotImplementedException();
        }

        private static void ReadAliasProvider(PolicyAliasProvider aliasProvider, JsonReader reader)
        {
            if (reader.TokenType != JsonToken.StartObject)
                throw new PipelineSerializationException(PSRuleResources.ReadJsonFailed);

            string providerName = null;

            reader.Read();
            while (reader.TokenType != JsonToken.EndObject)
            {
                switch (reader.TokenType)
                {
                    case JsonToken.PropertyName:
                        providerName = reader.Value.ToString();
                        break;

                    case JsonToken.StartObject:
                        var resourceType = ReadAliasResourceType(reader);
                        aliasProvider.Providers.Add(providerName, resourceType);
                        break;
                }
                reader.Read();
            }
        }

        private static PolicyAliasResourceType ReadAliasResourceType(JsonReader reader)
        {
            if (reader.TokenType != JsonToken.StartObject)
                throw new PipelineSerializationException(PSRuleResources.ReadJsonFailed);

            var aliasResourceType = new PolicyAliasResourceType();

            string resourceType = null;

            reader.Read();
            while (reader.TokenType != JsonToken.EndObject)
            {
                switch (reader.TokenType)
                {
                    case JsonToken.PropertyName:
                        resourceType = reader.Value.ToString();
                        break;

                    case JsonToken.StartObject:
                        var aliasMapping = ReadAliasMapping(reader);
                        aliasResourceType.ResourceTypes.Add(resourceType, aliasMapping);
                        break;
                }
                reader.Read();
            }

            return aliasResourceType;
        }

        private static PolicyAliasMapping ReadAliasMapping(JsonReader reader)
        {
            if (reader.TokenType != JsonToken.StartObject)
                throw new PipelineSerializationException(PSRuleResources.ReadJsonFailed);

            var aliasMapping = new PolicyAliasMapping();

            string aliasName = null;

            reader.Read();
            while (reader.TokenType != JsonToken.EndObject)
            {
                switch (reader.TokenType)
                {
                    case JsonToken.PropertyName:
                        aliasName = reader.Value.ToString();
                        break;

                    case JsonToken.String:
                        var aliasPath = reader.Value.ToString();
                        aliasMapping.AliasMappings.Add(aliasName, aliasPath);
                        break;
                }
                reader.Read();
            }

            return aliasMapping;
        }
    }

    internal sealed class PolicyDefinitionConverter : JsonConverter
    {
        public override bool CanConvert(Type objectType)
        {
            return objectType == typeof(PolicyDefinition);
        }

        public override bool CanWrite => true;

        public override bool CanRead => false;

        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            PolicyJsonRuleMapper.MapRule(writer, serializer, value as PolicyDefinition);
        }

        public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
        {
            throw new NotImplementedException();
        }
    }

    internal sealed class ResourceProviderConverter : JsonConverter
    {
        public override bool CanConvert(Type objectType)
        {
            return objectType == typeof(ResourceProvider) || objectType == typeof(Dictionary<string, ResourceProvider>);
        }

        public override bool CanRead => true;

        public override bool CanWrite => false;

        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            throw new NotImplementedException();
        }

        public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
        {
            if (objectType == typeof(ResourceProvider))
            {
                var resultObject = existingValue as ResourceProvider ?? new ResourceProvider();
                ReadObject(resultObject, reader, serializer);
                return resultObject;
            }

            var resultDictionary = existingValue as Dictionary<string, ResourceProvider> ?? new Dictionary<string, ResourceProvider>(StringComparer.OrdinalIgnoreCase);
            ReadDictionary(resultDictionary, reader, serializer);
            return resultDictionary;
        }

        private static void ReadObject(ResourceProvider value, JsonReader reader, JsonSerializer serializer)
        {
            if (reader.TokenType != JsonToken.StartObject)
                throw new PipelineSerializationException(PSRuleResources.ReadJsonFailed);

            reader.Read();
            string name = null;

            // Read each token
            while (reader.TokenType != JsonToken.EndObject)
            {
                switch (reader.TokenType)
                {
                    case JsonToken.PropertyName:
                        name = reader.Value.ToString();
                        break;

                    case JsonToken.StartObject:
                        if (name == "types")
                        {
                            ReadType(value, reader, serializer);
                        }
                        break;
                }
                reader.Read();
            }
        }

        private static void ReadType(ResourceProvider value, JsonReader reader, JsonSerializer serializer)
        {
            if (reader.TokenType != JsonToken.StartObject)
                throw new PipelineSerializationException(PSRuleResources.ReadJsonFailed);

            reader.Read();
            string name = null;

            // Read each token
            while (reader.TokenType != JsonToken.EndObject)
            {
                switch (reader.TokenType)
                {
                    case JsonToken.PropertyName:
                        name = reader.Value.ToString();
                        break;

                    case JsonToken.StartObject:
                        var resourceType = serializer.Deserialize<ResourceProviderType>(reader);
                        value.Types.Add(name, resourceType);
                        break;
                }
                reader.Read();
            }
        }

        private static void ReadDictionary(Dictionary<string, ResourceProvider> value, JsonReader reader, JsonSerializer serializer)
        {
            if (reader.TokenType != JsonToken.StartObject)
                throw new PipelineSerializationException(PSRuleResources.ReadJsonFailed);

            reader.Read();
            string name = null;

            // Read each token
            while (reader.TokenType != JsonToken.EndObject)
            {
                switch (reader.TokenType)
                {
                    case JsonToken.PropertyName:
                        name = reader.Value.ToString();
                        break;

                    case JsonToken.StartObject:
                        var provider = new ResourceProvider();
                        ReadObject(provider, reader, serializer);
                        value.Add(name, provider);
                        break;
                }
                reader.Read();
            }
        }
    }
}
