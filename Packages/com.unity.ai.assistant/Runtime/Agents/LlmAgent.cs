using System;
using System.Collections.Generic;
using System.Reflection;
using Unity.AI.Assistant.ApplicationModels;
using Unity.AI.Assistant.FunctionCalling;

namespace Unity.AI.Assistant.Agents
{
    /// <summary>
    /// An agent that leverages a Large Language Model (LLM) to answer a request
    /// </summary>
    class LlmAgent : BaseAgent<LlmAgent>
    {
        /// <summary>
        /// System prompt that defines the agent's behavior and instructions
        /// Use this to specialize your agent by providing your custom instructions
        /// </summary>
        public string SystemPrompt { get; set; }

        /// <summary>
        /// List of functions this agent can call
        /// </summary>
        internal List<FunctionDefinition> Tools { get; } = new();

        /// <summary>
        /// Constructor of an LLM Agent
        /// </summary>
        /// <param name="uniqueId">Unique identifier for this agent configuration</param>
        /// <param name="name">Display name for the agent</param>
        /// <param name="description">Description of what this agent does This is used by the multi-agents orchestration system to pick the best agent(s) for a given task</param>
        /// <param name="systemPrompt">System prompt that defines the agent's behavior and instructions</param>
        public LlmAgent(string uniqueId = "", string name = "", string description = "", string systemPrompt = "")
            : base(uniqueId, name, description)
        {
            SystemPrompt = systemPrompt;
        }

        /// <summary>
        /// Set the system prompt that defines the agent's behavior
        /// </summary>
        public LlmAgent WithSystemPrompt(string systemPrompt)
        {
            SystemPrompt = systemPrompt;
            return this;
        }

        public LlmAgent WithToolsFrom<T>()
        {
            var type = typeof(T);
            var methods = type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);

            foreach (var method in methods)
            {
                if (method.GetCustomAttribute<AgentToolAttribute>() == null)
                    continue;

                WithTool(method);
            }

            return this;
        }

        /// <summary>
        /// Add a native tool/function that the agent can call
        /// </summary>
        public LlmAgent WithTool(MethodInfo methodInfo)
        {
            // Ensure method has [AgentTool]
            var toolAttr = methodInfo.GetCustomAttribute<AgentToolAttribute>();
            if (toolAttr == null)
                throw new InvalidOperationException($"{methodInfo.Name} is not marked with [AgentTool]");

            if (!ToolRegistry.FunctionToolbox.HasFunction(toolAttr.Id))
                throw new InvalidOperationException($"{methodInfo.Name} is not registered as a toolbox tool");

            var toolDefinition = ToolRegistry.FunctionToolbox.GetFunctionDefinition(toolAttr.Id);
            RegisterFunctionDefinitionInternal(toolDefinition);

            return this;
        }

        /// <summary>
        /// Add a tool/function that the agent can call
        /// </summary>
        internal LlmAgent WithTool(FunctionDefinition functionDefinition)
        {
            RegisterFunctionDefinitionInternal(functionDefinition);
            return this;
        }

        /// <summary>
        /// Add multiple tools/functions that the agent can call
        /// </summary>
        internal LlmAgent WithTools(IEnumerable<FunctionDefinition> functionDefinitions)
        {
            foreach (var functionDefinition in functionDefinitions)
            {
                RegisterFunctionDefinitionInternal(functionDefinition);
            }
            return this;
        }

        void RegisterFunctionDefinitionInternal(FunctionDefinition functionDefinition)
        {
            if (Tools.Contains(functionDefinition))
                throw new InvalidOperationException($"Cannot add the same tool twice: {functionDefinition.Name}");

            Tools.Add(functionDefinition);
        }
    }
}
