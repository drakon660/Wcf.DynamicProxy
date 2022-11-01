using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.ServiceModel.Description;
using Labo.ServiceModel.Core.Utils.Reflection;
using Labo.ServiceModel.DynamicProxy;

namespace Labo.WcfTestClient.Win.Terminal
{
    internal class Program
    {
        public static void Main(string[] args)
        {
            Init("http://localhost:8667/sample.svc");
        }

        static void Init(string wsdl)
        {
            ServiceClientProxyFactoryGenerator proxyFactoryGenerator = new ServiceClientProxyFactoryGenerator(new ServiceMetadataDownloader(), new ServiceMetadataImporter(new CSharpCodeDomProviderFactory()), new ServiceClientProxyCompiler());
            ServiceClientProxyFactory proxyFactory = proxyFactoryGenerator.GenerateProxyFactory(wsdl);
            // List<ServiceInfo> serviceInfos = new List<ServiceInfo>();
            // ServiceInfo serviceInfo = new ServiceInfo { Wsdl = Wsdl, Config = proxyFactory.Config };
            for (int index = 0; index < proxyFactory.Contracts.Count; index++)
            {
                ContractDescription contractDescription = proxyFactory.Contracts[index];
                string contractName = contractDescription.Name;
                ServiceClientProxy proxy = proxyFactory.CreateProxy(contractName, contractDescription.Namespace);
                string[] operationNames = contractDescription.Operations.Select(x => x.Name).ToArray();
                
                string operationName = operationNames[0];
                object instance = proxy.CreateInstance();
                Method method = ReflectionUtils.GetMethodDefinition(instance, operationName);
                
                IDictionary<string, ReflectionUtils.Parameter> parameters = new Dictionary<string, ReflectionUtils.Parameter>();
                var result = ReflectionUtils.InvokeMethod(instance, operationName, parameters);
                
                Class @class = ReflectionUtils.GetClassDefinition(result.GetType());

                var props = @class.Properties;
                var status = result.GetType().GetProperty("Status").GetValue(result, null);
                Console.WriteLine(status);
                
                // ContractInfo contractInfo = new ContractInfo {Proxy = proxy, ContractName = contractName};
                //
                // for (int i = 0; i < operationNames.Length; i++)
                // {
                //     string operationName = operationNames[i];
                //     object instance = proxy.CreateInstance();
                //     using (instance as IDisposable)
                //     {
                //         Method method = ReflectionUtils.GetMethodDefinition(instance, operationName);
                //         contractInfo.Operations.Add(new OperationInfo {Contract = contractInfo, Method = method});
                //     }
                // }
                // serviceInfo.Contracts.Add(contractInfo);
            }
            // serviceInfos.Add(serviceInfo);
            //
            // m_Services = serviceInfos.AsReadOnly();
        }
    }
}