using System;
using System.Runtime.Serialization;
using System.ServiceModel;
using System.ServiceModel.Description;
using Autofac;
using Autofac.Integration.Wcf;

namespace Wcf.Sample
{
    internal class Program
    {
        public static void Main(string[] args)
        {
            AutofacBootstrapper("http://localhost:8667/sample.svc");
        }
        
        static void AutofacBootstrapper(string url)
        {
            ContainerBuilder builder = new ContainerBuilder();
            builder.RegisterType<SampleSvc>().As<ISampleSvc>();
            
            using (var container = builder.Build())
            {
                Uri address = new Uri(url);
                ServiceHost host = new ServiceHost(typeof(SampleSvc), address);
                host.AddServiceEndpoint(typeof(ISampleSvc), new BasicHttpBinding(),string.Empty);

                // Here's the important part - attaching the DI behavior to the service host
                // and passing in the container.
                host.AddDependencyInjectionBehavior<ISampleSvc>(container);
            
                host.Description.Behaviors.Add(new ServiceMetadataBehavior {HttpGetEnabled = true, HttpGetUrl = address , MetadataExporter = new WsdlExporter() {PolicyVersion = PolicyVersion.Policy15}});
                host.Open();
            
                Console.WriteLine("The host has been opened.");
                Console.ReadLine();
            
                host.Close();
                Environment.Exit(0);
            }
        }
    }

    [ServiceContract]
    interface ISampleSvc
    {
        [OperationContract]
        SampleSvcResponse Info();
    }

    class SampleSvc : ISampleSvc
    {
        public SampleSvcResponse Info()
        {
            return new SampleSvcResponse() { Status = "OK"};
        }
    }

    
    [DataContract]
    class SampleSvcResponse
    {
        [DataMember] public string Status { get; set; }
    }
}