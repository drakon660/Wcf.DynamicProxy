// See https://aka.ms/new-console-template for more information

using System.ServiceModel.Description;
using Labo.ServiceModel.Core.Utils.Reflection;
using Labo.ServiceModel.DynamicProxy;

Console.WriteLine("Hello, World!");
Init("http://localhost:8667/sample.svc");

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
                
            }
        }