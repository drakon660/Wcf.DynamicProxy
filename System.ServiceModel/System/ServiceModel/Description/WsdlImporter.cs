// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

namespace System.ServiceModel.Description
{
    using System.Collections;
    using System.Collections.Generic;
    using System.Collections.ObjectModel;
    using System.Globalization;
    using System.Runtime;
    using System.Security;
    using System.ServiceModel;
    using System.ServiceModel.Channels;
    using System.Text;
    using Microsoft.Xml;
    using Microsoft.Xml.Schema;
    using WsdlNS = System.Web.Services.Description;
    using WsdlConfigNS = System.Web.Services.Configuration;

    public class WsdlImporter : MetadataImporter
    {
        private readonly Dictionary<WsdlNS.NamedItem, WsdlImportException> _importErrors = new Dictionary<WsdlNS.NamedItem, WsdlImportException>();
        private bool _isFaulted = false;

        private readonly Dictionary<XmlQualifiedName, WsdlContractConversionContext> _importedPortTypes = new Dictionary<XmlQualifiedName, WsdlContractConversionContext>();
        private readonly Dictionary<XmlQualifiedName, WsdlEndpointConversionContext> _importedBindings = new Dictionary<XmlQualifiedName, WsdlEndpointConversionContext>();
        private readonly Dictionary<WsdlNS.Port, ServiceEndpoint> _importedPorts = new Dictionary<WsdlNS.Port, ServiceEndpoint>();

        private readonly KeyedByTypeCollection<IWsdlImportExtension> _wsdlExtensions;

        private readonly WsdlNS.ServiceDescriptionCollection _wsdlDocuments = new WsdlNS.ServiceDescriptionCollection();
        private readonly XmlSchemaSet _xmlSchemas = WsdlExporter.GetEmptySchemaSet();
        private readonly Dictionary<string, XmlElement> _policyDocuments = new Dictionary<string, XmlElement>();
        private readonly Dictionary<string, string> _warnings = new Dictionary<string, string>();
        private WsdlPolicyReader _wsdlPolicyReader;

        private bool _beforeImportCalled = false;

        public WsdlImporter(MetadataSet metadata)
            : this(metadata, null, null, MetadataImporterQuotas.Defaults)
        {
        }

        public WsdlImporter(MetadataSet metadata, IEnumerable<IPolicyImportExtension> policyImportExtensions,
            IEnumerable<IWsdlImportExtension> wsdlImportExtensions)
            : this(metadata, policyImportExtensions, wsdlImportExtensions, MetadataImporterQuotas.Defaults)
        {
        }

        public WsdlImporter(MetadataSet metadata, IEnumerable<IPolicyImportExtension> policyImportExtensions,
            IEnumerable<IWsdlImportExtension> wsdlImportExtensions, MetadataImporterQuotas quotas)
            : base(policyImportExtensions, quotas)
        {
            // process wsdl extensions first for consistency with policy extensions (which are processed in base ctor)
            if (wsdlImportExtensions == null)
            {
                wsdlImportExtensions = LoadWsdlExtensionsFromConfig();
            }

            _wsdlExtensions = new KeyedByTypeCollection<IWsdlImportExtension>(wsdlImportExtensions);

            // then look at metadata
            if (metadata == null)
                throw DiagnosticUtility.ExceptionUtility.ThrowHelperArgumentNull("metadata");

            this.ProcessMetadataDocuments(metadata.MetadataSections);
        }

        public KeyedByTypeCollection<IWsdlImportExtension> WsdlImportExtensions
        {
            get { return _wsdlExtensions; }
        }

        public WsdlNS.ServiceDescriptionCollection WsdlDocuments
        {
            get { return _wsdlDocuments; }
        }

        public XmlSchemaSet XmlSchemas
        {
            get { return _xmlSchemas; }
        }

        private WsdlPolicyReader PolicyReader
        {
            get
            {
                if (_wsdlPolicyReader == null)
                {
                    _wsdlPolicyReader = new WsdlPolicyReader(this);
                }
                return _wsdlPolicyReader;
            }
        }

        //Consider this should be made public
        internal override XmlElement ResolvePolicyReference(string policyReference, XmlElement contextAssertion)
        {
            return this.PolicyReader.ResolvePolicyReference(policyReference, contextAssertion);
        }

        public override Collection<ContractDescription> ImportAllContracts()
        {
            if (_isFaulted)
                throw DiagnosticUtility.ExceptionUtility.ThrowHelperError(new InvalidOperationException(SRServiceModel.WsdlImporterIsFaulted));

            EnsureBeforeImportCalled();
            Collection<ContractDescription> contracts = new Collection<ContractDescription>();
            foreach (WsdlNS.ServiceDescription wsdl in _wsdlDocuments)
                foreach (WsdlNS.PortType wsdlPortType in wsdl.PortTypes)
                {
                    if (IsBlockedListed(wsdlPortType))
                        continue;

                    ContractDescription contract = ImportWsdlPortType(wsdlPortType, WsdlPortTypeImportOptions.ReuseExistingContracts, ErrorBehavior.DoNotThrowExceptions);
                    if (contract != null)
                        contracts.Add(contract);
                }
            return contracts;
        }

        public override ServiceEndpointCollection ImportAllEndpoints()
        {
            if (_isFaulted)
                throw DiagnosticUtility.ExceptionUtility.ThrowHelperError(new InvalidOperationException(SRServiceModel.WsdlImporterIsFaulted));

            EnsureBeforeImportCalled();
            ServiceEndpointCollection endpoints = new ServiceEndpointCollection();
            foreach (WsdlNS.Port wsdlPort in this.GetAllPorts())
            {
                if (IsBlockedListed(wsdlPort))
                    continue;

                ServiceEndpoint endpoint = ImportWsdlPort(wsdlPort, ErrorBehavior.DoNotThrowExceptions);
                if (endpoint != null)
                    endpoints.Add(endpoint);
            }
            return endpoints;
        }


        public Collection<Binding> ImportAllBindings()
        {
            if (_isFaulted)
                throw DiagnosticUtility.ExceptionUtility.ThrowHelperError(new InvalidOperationException(SRServiceModel.WsdlImporterIsFaulted));

            EnsureBeforeImportCalled();
            Collection<Binding> bindings = new Collection<Binding>();
            foreach (WsdlNS.Binding wsdlBinding in this.GetAllBindings())
            {
                WsdlEndpointConversionContext importedBindingContext = null;
                if (IsBlockedListed(wsdlBinding))
                    continue;

                importedBindingContext = ImportWsdlBinding(wsdlBinding, ErrorBehavior.DoNotThrowExceptions);
                if (importedBindingContext != null)
                    bindings.Add(importedBindingContext.Endpoint.Binding);
            }
            return bindings;
        }

        // WSDL Specific methods
        public ContractDescription ImportContract(WsdlNS.PortType wsdlPortType)
        {
            if (_isFaulted)
                throw DiagnosticUtility.ExceptionUtility.ThrowHelperError(new InvalidOperationException(SRServiceModel.WsdlImporterIsFaulted));

            if (wsdlPortType == null)
                throw DiagnosticUtility.ExceptionUtility.ThrowHelperArgumentNull("wsdlPortType");

            return ImportWsdlPortType(wsdlPortType, WsdlPortTypeImportOptions.ReuseExistingContracts, ErrorBehavior.RethrowExceptions);
        }

        public Binding ImportBinding(WsdlNS.Binding wsdlBinding)
        {
            if (_isFaulted)
                throw DiagnosticUtility.ExceptionUtility.ThrowHelperError(new InvalidOperationException(SRServiceModel.WsdlImporterIsFaulted));

            if (wsdlBinding == null)
                throw DiagnosticUtility.ExceptionUtility.ThrowHelperArgumentNull("wsdlBinding");

            return ImportWsdlBinding(wsdlBinding, ErrorBehavior.RethrowExceptions).Endpoint.Binding;
        }

        public ServiceEndpoint ImportEndpoint(WsdlNS.Port wsdlPort)
        {
            if (_isFaulted)
                throw DiagnosticUtility.ExceptionUtility.ThrowHelperError(new InvalidOperationException(SRServiceModel.WsdlImporterIsFaulted));

            if (wsdlPort == null)
                throw DiagnosticUtility.ExceptionUtility.ThrowHelperArgumentNull("wsdlPort");

            return ImportWsdlPort(wsdlPort, ErrorBehavior.RethrowExceptions);
        }

        public ServiceEndpointCollection ImportEndpoints(WsdlNS.PortType wsdlPortType)
        {
            if (_isFaulted)
                throw DiagnosticUtility.ExceptionUtility.ThrowHelperError(new InvalidOperationException(SRServiceModel.WsdlImporterIsFaulted));

            if (wsdlPortType == null)
                throw DiagnosticUtility.ExceptionUtility.ThrowHelperArgumentNull("wsdlPortType");

            if (IsBlockedListed(wsdlPortType))
                throw CreateAlreadyFaultedException(wsdlPortType);
            else
                ImportWsdlPortType(wsdlPortType, WsdlPortTypeImportOptions.ReuseExistingContracts, ErrorBehavior.RethrowExceptions);

            ServiceEndpointCollection endpoints = new ServiceEndpointCollection();

            foreach (WsdlNS.Binding wsdlBinding in FindBindingsForPortType(wsdlPortType))
                if (!IsBlockedListed(wsdlBinding))
                    foreach (ServiceEndpoint endpoint in ImportEndpoints(wsdlBinding))
                        endpoints.Add(endpoint);

            return endpoints;
        }

        internal ServiceEndpointCollection ImportEndpoints(ContractDescription contract)
        {
            if (_isFaulted)
                throw DiagnosticUtility.ExceptionUtility.ThrowHelperError(new InvalidOperationException(SRServiceModel.WsdlImporterIsFaulted));

            if (contract == null)
                throw DiagnosticUtility.ExceptionUtility.ThrowHelperArgumentNull("contract");

            if (!this.KnownContracts.ContainsKey(WsdlExporter.WsdlNamingHelper.GetPortTypeQName(contract)))
            {
                Fx.Assert("WsdlImporter.ImportEndpoints(ContractDescription contract): !this.KnownContracts.ContainsKey(WsdlExporter.WsdlNamingHelper.GetPortTypeQName(contract))");
                throw DiagnosticUtility.ExceptionUtility.ThrowHelperError(new ArgumentException(SRServiceModel.WsdlImporterContractMustBeInKnownContracts));
            }

            EnsureBeforeImportCalled();
            ServiceEndpointCollection endpoints = new ServiceEndpointCollection();

            foreach (WsdlNS.Binding wsdlBinding in FindBindingsForContract(contract))
                if (!IsBlockedListed(wsdlBinding))
                    foreach (ServiceEndpoint endpoint in ImportEndpoints(wsdlBinding))
                        endpoints.Add(endpoint);

            return endpoints;
        }

        public ServiceEndpointCollection ImportEndpoints(WsdlNS.Binding wsdlBinding)
        {
            if (_isFaulted)
                throw DiagnosticUtility.ExceptionUtility.ThrowHelperError(new InvalidOperationException(SRServiceModel.WsdlImporterIsFaulted));

            if (wsdlBinding == null)
                throw DiagnosticUtility.ExceptionUtility.ThrowHelperArgumentNull("wsdlBinding");

            if (IsBlockedListed(wsdlBinding))
                throw CreateAlreadyFaultedException(wsdlBinding);
            else
                ImportWsdlBinding(wsdlBinding, ErrorBehavior.RethrowExceptions);

            ServiceEndpointCollection endpoints = new ServiceEndpointCollection();

            foreach (WsdlNS.Port wsdlPort in FindPortsForBinding(wsdlBinding))
                if (!IsBlockedListed(wsdlPort))
                {
                    ServiceEndpoint endpoint = ImportWsdlPort(wsdlPort, ErrorBehavior.DoNotThrowExceptions);
                    if (endpoint != null)
                        endpoints.Add(endpoint);
                }

            return endpoints;
        }

        public ServiceEndpointCollection ImportEndpoints(WsdlNS.Service wsdlService)
        {
            if (_isFaulted)
                throw DiagnosticUtility.ExceptionUtility.ThrowHelperError(new InvalidOperationException(SRServiceModel.WsdlImporterIsFaulted));

            if (wsdlService == null)
                throw DiagnosticUtility.ExceptionUtility.ThrowHelperArgumentNull("wsdlService");

            EnsureBeforeImportCalled();
            ServiceEndpointCollection endpoints = new ServiceEndpointCollection();

            foreach (WsdlNS.Port wsdlPort in wsdlService.Ports)
                if (!IsBlockedListed(wsdlPort))
                {
                    ServiceEndpoint endpoint = ImportWsdlPort(wsdlPort, ErrorBehavior.DoNotThrowExceptions);
                    if (endpoint != null)
                        endpoints.Add(endpoint);
                }

            return endpoints;
        }

        private bool IsBlockedListed(WsdlNS.NamedItem item)
        {
            return _importErrors.ContainsKey(item);
        }

        private ContractDescription ImportWsdlPortType(WsdlNS.PortType wsdlPortType, WsdlPortTypeImportOptions importOptions, ErrorBehavior errorBehavior)
        {
            if (IsBlockedListed(wsdlPortType))
                throw DiagnosticUtility.ExceptionUtility.ThrowHelperError(CreateAlreadyFaultedException(wsdlPortType));


            XmlQualifiedName wsdlPortTypeQName = new XmlQualifiedName(wsdlPortType.Name, wsdlPortType.ServiceDescription.TargetNamespace);
            ContractDescription contractDescription = null;

            if (importOptions == WsdlPortTypeImportOptions.IgnoreExistingContracts || !TryFindExistingContract(wsdlPortTypeQName, out contractDescription))
            {
                EnsureBeforeImportCalled();

                try
                {
                    contractDescription = CreateContractDescription(wsdlPortType, wsdlPortTypeQName);
                    WsdlContractConversionContext contractContext = new WsdlContractConversionContext(contractDescription, wsdlPortType);

                    foreach (WsdlNS.Operation wsdlOperation in wsdlPortType.Operations)
                    {
                        OperationDescription operationDescription = CreateOperationDescription(wsdlPortType, wsdlOperation, contractDescription);
                        contractContext.AddOperation(operationDescription, wsdlOperation);

                        foreach (WsdlNS.OperationMessage wsdlOperationMessage in wsdlOperation.Messages)
                        {
                            MessageDescription messageDescription;
                            if (TryCreateMessageDescription(wsdlOperationMessage, operationDescription, out messageDescription))
                                contractContext.AddMessage(messageDescription, wsdlOperationMessage);
                        }

                        foreach (WsdlNS.OperationFault wsdlOperationFault in wsdlOperation.Faults)
                        {
                            FaultDescription faultDescription;
                            if (TryCreateFaultDescription(wsdlOperationFault, operationDescription, out faultDescription))
                                contractContext.AddFault(faultDescription, wsdlOperationFault);
                        }
                    }

                    CallImportContract(contractContext);
                    VerifyImportedWsdlPortType(wsdlPortType);

                    _importedPortTypes.Add(wsdlPortTypeQName, contractContext);
                }
#pragma warning disable 56500 // covered by FxCOP
                catch (Exception e)
                {
                    if (Fx.IsFatal(e))
                        throw;
                    WsdlImportException wie = WsdlImportException.Create(wsdlPortType, e);
                    LogImportError(wsdlPortType, wie, isWarning: errorBehavior == ErrorBehavior.DoNotThrowExceptions);
                    if (errorBehavior == ErrorBehavior.RethrowExceptions)
                        throw DiagnosticUtility.ExceptionUtility.ThrowHelperError(wie);
                    else
                        return null;
                }
            }

            return contractDescription;
        }

        private WsdlEndpointConversionContext ImportWsdlBinding(WsdlNS.Binding wsdlBinding, ErrorBehavior errorBehavior)
        {
            //Check for exisiting exception
            if (IsBlockedListed(wsdlBinding))
                throw DiagnosticUtility.ExceptionUtility.ThrowHelperError(CreateAlreadyFaultedException(wsdlBinding));

            XmlQualifiedName wsdlBindingQName = new XmlQualifiedName(wsdlBinding.Name, wsdlBinding.ServiceDescription.TargetNamespace);
            WsdlEndpointConversionContext bindingEndpointContext = null;




            if (!_importedBindings.TryGetValue(wsdlBindingQName, out bindingEndpointContext))
            {
                EnsureBeforeImportCalled();

                try
                {
                    bool wasExistingContract;
                    ContractDescription contractDescription = GetOrImportContractDescription(wsdlBinding.Type, out wasExistingContract);
                    WsdlContractConversionContext contractContext = null;
                    _importedPortTypes.TryGetValue(wsdlBinding.Type, out contractContext);

                    ServiceEndpoint newWsdlBindingEndpoint = new ServiceEndpoint(contractDescription);
                    bindingEndpointContext = new WsdlEndpointConversionContext(contractContext, newWsdlBindingEndpoint, wsdlBinding, null);

                    foreach (WsdlNS.OperationBinding wsdlOperationBinding in wsdlBinding.Operations)
                        try
                        {
                            OperationDescription operation = Binding2DescriptionHelper.FindOperationDescription(wsdlOperationBinding, _wsdlDocuments, bindingEndpointContext);
                            bindingEndpointContext.AddOperationBinding(operation, wsdlOperationBinding);

                            for (int i = 0; i < operation.Messages.Count; i++)
                            {
                                MessageDescription message = operation.Messages[i];
                                WsdlNS.MessageBinding wsdlMessageBinding = Binding2DescriptionHelper.FindMessageBinding(wsdlOperationBinding, message);

                                bindingEndpointContext.AddMessageBinding(message, wsdlMessageBinding);
                            }

                            foreach (FaultDescription fault in operation.Faults)
                            {
                                WsdlNS.FaultBinding wsdlFaultBinding = Binding2DescriptionHelper.FindFaultBinding(wsdlOperationBinding, fault);
                                if (wsdlFaultBinding != null)
                                {
                                    bindingEndpointContext.AddFaultBinding(fault, wsdlFaultBinding);
                                }
                            }
                        }
#pragma warning disable 56500 // covered by FxCOP
                        catch (Exception e)
                        {
                            if (Fx.IsFatal(e))
                                throw;
                            throw DiagnosticUtility.ExceptionUtility.ThrowHelperError(WsdlImportException.Create(wsdlOperationBinding, e));
                        }

                    XmlQualifiedName bindingQName = WsdlNamingHelper.GetBindingName(wsdlBinding);
                    newWsdlBindingEndpoint.Binding = CreateBinding(bindingEndpointContext, bindingQName);

                    CallImportEndpoint(bindingEndpointContext);
                    VerifyImportedWsdlBinding(wsdlBinding);
                    _importedBindings.Add(wsdlBindingQName, bindingEndpointContext);
                }
#pragma warning disable 56500 // covered by FxCOP
                catch (Exception e)
                {
                    if (Fx.IsFatal(e))
                        throw;
                    WsdlImportException wie = WsdlImportException.Create(wsdlBinding, e);
                    LogImportError(wsdlBinding, wie, isWarning: errorBehavior == ErrorBehavior.DoNotThrowExceptions);
                    if (errorBehavior == ErrorBehavior.RethrowExceptions)
                        throw DiagnosticUtility.ExceptionUtility.ThrowHelperError(wie);
                    else
                        return null;
                }
            }
            return bindingEndpointContext;
        }

        private ServiceEndpoint ImportWsdlPort(WsdlNS.Port wsdlPort, ErrorBehavior errorBehavior)
        {
            if (IsBlockedListed(wsdlPort))
                throw DiagnosticUtility.ExceptionUtility.ThrowHelperError(CreateAlreadyFaultedException(wsdlPort));


            ServiceEndpoint endpoint = null;
            if (!_importedPorts.TryGetValue(wsdlPort, out endpoint))
            {
                EnsureBeforeImportCalled();

                try
                {
                    WsdlNS.Binding wsdlBinding = _wsdlDocuments.GetBinding(wsdlPort.Binding);

                    WsdlEndpointConversionContext bindingEndpointContext = ImportWsdlBinding(wsdlBinding, errorBehavior);
                    if (bindingEndpointContext == null)
                    {
                        throw WsdlImportException.Create(wsdlPort, null);
                    }

                    WsdlEndpointConversionContext endpointContext;
                    endpoint = new ServiceEndpoint(bindingEndpointContext.Endpoint.Contract);
                    endpoint.Name = WsdlNamingHelper.GetEndpointName(wsdlPort).EncodedName;

                    endpointContext = new WsdlEndpointConversionContext(bindingEndpointContext, endpoint, wsdlPort);

                    if (WsdlPolicyReader.HasPolicy(wsdlPort))
                    {
                        XmlQualifiedName bindingQName = WsdlNamingHelper.GetBindingName(wsdlPort);
                        endpoint.Binding = CreateBinding(endpointContext, bindingQName);
                    }
                    else
                    {
                        endpoint.Binding = bindingEndpointContext.Endpoint.Binding;
                    }

                    CallImportEndpoint(endpointContext);
                    VerifyImportedWsdlPort(wsdlPort);
                    _importedPorts.Add(wsdlPort, endpoint);
                }
#pragma warning disable 56500 // covered by FxCOP
                catch (Exception e)
                {
                    if (Fx.IsFatal(e))
                        throw;
                    WsdlImportException wie = WsdlImportException.Create(wsdlPort, e);
                    LogImportError(wsdlPort, wie, isWarning: errorBehavior == ErrorBehavior.DoNotThrowExceptions);
                    if (errorBehavior == ErrorBehavior.RethrowExceptions)
                        throw DiagnosticUtility.ExceptionUtility.ThrowHelperError(wie);
                    else
                        return null;
                }
            }

            return endpoint;
        }

        private static bool TryCreateMessageDescription(WsdlNS.OperationMessage wsdlOperationMessage, OperationDescription operationDescription, out MessageDescription messageDescription)
        {
            string actionUri = WSAddressingHelper.GetWsaActionUri(wsdlOperationMessage);
            MessageDirection direction;

            if (wsdlOperationMessage is WsdlNS.OperationInput)
                direction = MessageDirection.Input;
            else if (wsdlOperationMessage is WsdlNS.OperationOutput)
                direction = MessageDirection.Output;
            else
            {
                messageDescription = null;
                return false;
            }

            messageDescription = new MessageDescription(actionUri, direction);
            messageDescription.MessageName = WsdlNamingHelper.GetOperationMessageName(wsdlOperationMessage);
            messageDescription.XsdTypeName = wsdlOperationMessage.Message;
            operationDescription.Messages.Add(messageDescription);
            return true;
        }

        private static bool TryCreateFaultDescription(WsdlNS.OperationFault wsdlOperationFault, OperationDescription operationDescription, out FaultDescription faultDescription)
        {
            if (string.IsNullOrEmpty(wsdlOperationFault.Name))
            {
                faultDescription = null;
                return false;
            }

            string actionUri = WSAddressingHelper.GetWsaActionUri(wsdlOperationFault);
            faultDescription = new FaultDescription(actionUri);
            faultDescription.SetNameOnly(new XmlName(wsdlOperationFault.Name, true /*isEncoded*/));
            operationDescription.Faults.Add(faultDescription);
            return true;
        }

        private ContractDescription CreateContractDescription(WsdlNS.PortType wsdlPortType, XmlQualifiedName wsdlPortTypeQName)
        {
            ContractDescription contractDescription;
            XmlQualifiedName contractQName = WsdlNamingHelper.GetContractName(wsdlPortTypeQName);
            contractDescription = new ContractDescription(contractQName.Name, contractQName.Namespace);
            NetSessionHelper.SetSession(contractDescription, wsdlPortType);
            return contractDescription;
        }

        private OperationDescription CreateOperationDescription(WsdlNS.PortType wsdlPortType, WsdlNS.Operation wsdlOperation, ContractDescription contract)
        {
            string operationName = WsdlNamingHelper.GetOperationName(wsdlOperation);
            OperationDescription operationDescription = new OperationDescription(operationName, contract);
            NetSessionHelper.SetInitiatingTerminating(operationDescription, wsdlOperation);
            contract.Operations.Add(operationDescription);
            return operationDescription;
        }

        private Binding CreateBinding(WsdlEndpointConversionContext endpointContext, XmlQualifiedName bindingQName)
        {
            try
            {
                // either the wsdl:binding has already been imported or the wsdl:port has policy
                BindingElementCollection bindingElements = ImportPolicyFromWsdl(endpointContext);
                CustomBinding binding = new CustomBinding(bindingElements);
                // use decoded form to preserve the user-given friendly name  
                binding.Name = NamingHelper.CodeName(bindingQName.Name);
                binding.Namespace = bindingQName.Namespace;

                return binding;
            }
#pragma warning disable 56500 // covered by FxCOP
            catch (Exception e)
            {
                if (Fx.IsFatal(e))
                    throw;
                throw DiagnosticUtility.ExceptionUtility.ThrowHelperError(WsdlImportException.Create(endpointContext.WsdlBinding, e));
            }
        }

        private ContractDescription GetOrImportContractDescription(XmlQualifiedName wsdlPortTypeQName, out bool wasExistingContractDescription)
        {
            ContractDescription contractDescription;

            if (!TryFindExistingContract(wsdlPortTypeQName, out contractDescription))
            {
                WsdlNS.PortType wsdlPortType = _wsdlDocuments.GetPortType(wsdlPortTypeQName);

                contractDescription = ImportWsdlPortType(wsdlPortType, WsdlPortTypeImportOptions.IgnoreExistingContracts, ErrorBehavior.RethrowExceptions);

                wasExistingContractDescription = false;
            }

            wasExistingContractDescription = true;
            return contractDescription;
        }

        private void ProcessMetadataDocuments(IEnumerable<MetadataSection> metadataSections)
        {
            foreach (MetadataSection doc in metadataSections)
            {
                try
                {
                    if (doc.Metadata is MetadataReference || doc.Metadata is MetadataLocation)
                        continue;

                    if (doc.Dialect == MetadataSection.ServiceDescriptionDialect)
                    {
                        _wsdlDocuments.Add(TryConvert<WsdlNS.ServiceDescription>(doc));
                    }
                    if (doc.Dialect == MetadataSection.XmlSchemaDialect)
                    {
                        _xmlSchemas.Add(TryConvert<XmlSchema>(doc));
                    }
                    if (doc.Dialect == MetadataSection.PolicyDialect)
                    {
                        if (string.IsNullOrEmpty(doc.Identifier))
                        {
                            LogImportWarning(SRServiceModel.PolicyDocumentMustHaveIdentifier);
                        }
                        else
                        {
                            _policyDocuments.Add(doc.Identifier, TryConvert<XmlElement>(doc));
                        }
                    }
                }
#pragma warning disable 56500 // covered by FxCOP
                catch (Exception e)
                {
                    if (Fx.IsFatal(e))
                        throw;
                    throw DiagnosticUtility.ExceptionUtility.ThrowHelperError(new ArgumentException(doc.Identifier, e));
                }
            }
        }

        private T TryConvert<T>(MetadataSection doc)
        {
            try
            {
                return ((T)doc.Metadata);
            }
            catch (InvalidCastException)
            {
                throw DiagnosticUtility.ExceptionUtility.ThrowHelperError(new NotSupportedException(
                    string.Format(SRServiceModel.SFxBadMetadataDialect, doc.Identifier, doc.Dialect, typeof(T).FullName, doc.GetType().FullName)));
            }
        }

        private bool TryFindExistingContract(XmlQualifiedName wsdlPortTypeQName, out ContractDescription existingContract)
        {
            XmlQualifiedName contractQName = WsdlNamingHelper.GetContractName(wsdlPortTypeQName);

            // Scan Known Contracts first because wsdl:portType may not be available
            if (this.KnownContracts.TryGetValue(contractQName, out existingContract))
                return true;

            WsdlContractConversionContext contractContext;
            if (_importedPortTypes.TryGetValue(wsdlPortTypeQName, out contractContext))
            {
                existingContract = contractContext.Contract;
                return true;
            }

            return false;
        }

        private void EnsureBeforeImportCalled()
        {
            if (!_beforeImportCalled)
            {
                foreach (IWsdlImportExtension extension in _wsdlExtensions)
                {
                    try
                    {
                        extension.BeforeImport(_wsdlDocuments, _xmlSchemas, _policyDocuments.Values);
                    }
#pragma warning disable 56500 // covered by FxCOP
                    catch (Exception e)
                    {
                        _isFaulted = true;
                        if (Fx.IsFatal(e))
                            throw;
                        throw DiagnosticUtility.ExceptionUtility.ThrowHelperError(CreateBeforeImportExtensionException(extension, e));
                    }
                }
                _beforeImportCalled = true;
            }
        }

        private void CallImportContract(WsdlContractConversionContext contractConversionContext)
        {
            foreach (IWsdlImportExtension extension in _wsdlExtensions)
                try
                {
                    extension.ImportContract(this, contractConversionContext);
                }
#pragma warning disable 56500 // covered by FxCOP
                catch (Exception e)
                {
                    if (Fx.IsFatal(e))
                        throw;
                    throw DiagnosticUtility.ExceptionUtility.ThrowHelperError(CreateExtensionException(extension, e));
                }
        }

        private void CallImportEndpoint(WsdlEndpointConversionContext endpointConversionContext)
        {
            foreach (IWsdlImportExtension extension in _wsdlExtensions)
                try
                {
                    extension.ImportEndpoint(this, endpointConversionContext);
                }
#pragma warning disable 56500 // covered by FxCOP
                catch (Exception e)
                {
                    if (Fx.IsFatal(e))
                        throw;
                    throw DiagnosticUtility.ExceptionUtility.ThrowHelperError(CreateExtensionException(extension, e));
                }
        }

        private void VerifyImportedWsdlPortType(WsdlNS.PortType wsdlPortType)
        {
            VerifyImportedExtensions(wsdlPortType);
            foreach (WsdlNS.Operation wsdlOperation in wsdlPortType.Operations)
            {
                VerifyImportedExtensions(wsdlOperation);

                foreach (WsdlNS.OperationMessage wsdlOperationMessage in wsdlOperation.Messages)
                {
                    VerifyImportedExtensions(wsdlOperationMessage);
                }

                foreach (WsdlNS.OperationMessage wsdlOperationMessage in wsdlOperation.Faults)
                {
                    VerifyImportedExtensions(wsdlOperationMessage);
                }
            }
        }

        private void VerifyImportedWsdlBinding(WsdlNS.Binding wsdlBinding)
        {
            VerifyImportedExtensions(wsdlBinding);
            foreach (WsdlNS.OperationBinding wsdlOperationBinding in wsdlBinding.Operations)
            {
                VerifyImportedExtensions(wsdlOperationBinding);

                if (wsdlOperationBinding.Input != null)
                {
                    VerifyImportedExtensions(wsdlOperationBinding.Input);
                }

                if (wsdlOperationBinding.Output != null)
                {
                    VerifyImportedExtensions(wsdlOperationBinding.Output);
                }

                foreach (WsdlNS.MessageBinding wsdlMessageBinding in wsdlOperationBinding.Faults)
                {
                    VerifyImportedExtensions(wsdlMessageBinding);
                }
            }
        }

        private void VerifyImportedWsdlPort(WsdlNS.Port wsdlPort)
        {
            VerifyImportedExtensions(wsdlPort);
        }

        private void VerifyImportedExtensions(WsdlNS.NamedItem item)
        {
            foreach (object ext in item.Extensions)
            {
                if (item.Extensions.IsHandled(ext))
                    continue;

                XmlQualifiedName qName = GetUnhandledExtensionQName(ext, item);

                if (item.Extensions.IsRequired(ext) || IsNonSoapWsdl11BindingExtension(ext))
                {
                    string errorMsg = string.Format(SRServiceModel.RequiredWSDLExtensionIgnored, qName.Name, qName.Namespace);
                    WsdlImportException wie = WsdlImportException.Create(item, new InvalidOperationException(errorMsg));
                    throw DiagnosticUtility.ExceptionUtility.ThrowHelperError(wie);
                }
                else
                {
                    string xPath = CreateXPathString(item);
                    string errorMsg = string.Format(SRServiceModel.OptionalWSDLExtensionIgnored, qName.Name, qName.Namespace, xPath);
                    this.Errors.Add(new MetadataConversionError(errorMsg, true));
                }
            }
        }

        private static bool IsNonSoapWsdl11BindingExtension(object ext)
        {
            if (ext is WsdlNS.HttpAddressBinding
                || ext is WsdlNS.HttpBinding
                || ext is WsdlNS.HttpOperationBinding
                || ext is WsdlNS.HttpUrlEncodedBinding
                || ext is WsdlNS.HttpUrlReplacementBinding
                || ext is WsdlNS.MimeContentBinding
                || ext is WsdlNS.MimeMultipartRelatedBinding
                || ext is WsdlNS.MimePart
                || ext is WsdlNS.MimeTextBinding
                || ext is WsdlNS.MimeXmlBinding
                )
            {
                return true;
            }

            return false;
        }

        private XmlQualifiedName GetUnhandledExtensionQName(object extension, WsdlNS.NamedItem item)
        {
            XmlElement element = extension as XmlElement;
            if (element != null)
            {
                return new XmlQualifiedName(element.LocalName, element.NamespaceURI);
            }
            else if (extension is WsdlNS.ServiceDescriptionFormatExtension)
            {
                var xfeAttributes = ServiceReflector.GetCustomAttributes(extension.GetType(), typeof(WsdlConfigNS.XmlFormatExtensionAttribute), false);
                if (xfeAttributes.Length > 0)
                {
                    WsdlConfigNS.XmlFormatExtensionAttribute xmlAttrib = xfeAttributes[0] as WsdlConfigNS.XmlFormatExtensionAttribute;
                    if (xmlAttrib != null)
                    {
                        return new XmlQualifiedName(xmlAttrib.ElementName, xmlAttrib.Namespace);
                    }
                }
            }
            WsdlImportException wie = WsdlImportException.Create(item, new InvalidOperationException(string.Format(SRServiceModel.UnknownWSDLExtensionIgnored, extension.GetType().AssemblyQualifiedName)));
            throw DiagnosticUtility.ExceptionUtility.ThrowHelperError(wie);
        }

        internal static class Binding2DescriptionHelper
        {
            internal static OperationDescription FindOperationDescription(WsdlNS.OperationBinding wsdlOperationBinding, WsdlNS.ServiceDescriptionCollection wsdlDocuments, WsdlEndpointConversionContext endpointContext)
            {
                OperationDescription operation;
                if (endpointContext.ContractConversionContext != null)
                {
                    WsdlNS.Operation wsdlOperation = FindWsdlOperation(wsdlOperationBinding, wsdlDocuments);
                    operation = endpointContext.ContractConversionContext.GetOperationDescription(wsdlOperation);
                }
                else
                {
                    operation = FindOperationDescription(endpointContext.Endpoint.Contract, wsdlOperationBinding);
                }
                return operation;
            }

            internal static WsdlNS.MessageBinding FindMessageBinding(WsdlNS.OperationBinding wsdlOperationBinding, MessageDescription message)
            {
                WsdlNS.MessageBinding wsdlMessageBinding;
                if (message.Direction == MessageDirection.Input)
                {
                    wsdlMessageBinding = wsdlOperationBinding.Input;
                }
                else
                {
                    wsdlMessageBinding = wsdlOperationBinding.Output;
                }
                return wsdlMessageBinding;
            }

            internal static WsdlNS.FaultBinding FindFaultBinding(WsdlNS.OperationBinding wsdlOperationBinding, FaultDescription fault)
            {
                foreach (WsdlNS.FaultBinding faultBinding in wsdlOperationBinding.Faults)
                    if (faultBinding.Name == fault.Name)
                        return faultBinding;
                return null;
            }

            private static WsdlNS.Operation FindWsdlOperation(WsdlNS.OperationBinding wsdlOperationBinding, WsdlNS.ServiceDescriptionCollection wsdlDocuments)
            {
                WsdlNS.PortType wsdlPortType = wsdlDocuments.GetPortType(wsdlOperationBinding.Binding.Type);

                string wsdlOperationBindingName = wsdlOperationBinding.Name;

                if (wsdlOperationBindingName == null)
                {
                    throw DiagnosticUtility.ExceptionUtility.ThrowHelperError(new InvalidOperationException(string.Format(SRServiceModel.SFxInvalidWsdlBindingOpNoName, wsdlOperationBinding.Binding.Name)));
                }

                WsdlNS.Operation partialMatchResult = null;

                foreach (WsdlNS.Operation wsdlOperation in wsdlPortType.Operations)
                {
                    switch (Match(wsdlOperationBinding, wsdlOperation))
                    {
                        case MatchResult.None:
                            break;
                        case MatchResult.Partial:
                            partialMatchResult = wsdlOperation;
                            break;
                        case MatchResult.Exact:
                            return wsdlOperation;
                        default:
                            Fx.AssertAndFailFast("Unexpected MatchResult value.");
                            break;
                    }
                }

                if (partialMatchResult != null)
                {
                    return partialMatchResult;
                }
                else
                {
                    //unable to find wsdloperation for wsdlOperationBinding, invalid wsdl binding
                    throw DiagnosticUtility.ExceptionUtility.ThrowHelperError(new InvalidOperationException(string.Format(SRServiceModel.SFxInvalidWsdlBindingOpMismatch2, wsdlOperationBinding.Binding.Name, wsdlOperationBinding.Name)));
                }
            }

            internal enum MatchResult
            {
                None,
                Partial,
                Exact
            }

            internal static MatchResult Match(WsdlNS.OperationBinding wsdlOperationBinding, WsdlNS.Operation wsdlOperation)
            {
                // This method checks if there is a match based on Names, between the specified OperationBinding and Operation.
                // When searching for the Operation associated with an OperationBinding, we need to return an exact match if possible,
                // or a partial match otherwise (when some of the Names are null).
                // Bug 16833 @ CSDMain requires that partial matches are allowed, while the TFS bug 477838 requires that exact matches are done (when possible).
                if (wsdlOperationBinding.Name != wsdlOperation.Name)
                {
                    return MatchResult.None;
                }

                MatchResult result = MatchResult.Exact;

                foreach (WsdlNS.OperationMessage wsdlOperationMessage in wsdlOperation.Messages)
                {
                    WsdlNS.MessageBinding wsdlMessageBinding;
                    if (wsdlOperationMessage is WsdlNS.OperationInput)
                        wsdlMessageBinding = wsdlOperationBinding.Input;
                    else
                        wsdlMessageBinding = wsdlOperationBinding.Output;

                    if (wsdlMessageBinding == null)
                    {
                        return MatchResult.None;
                    }

                    switch (MatchOperationParameterName(wsdlMessageBinding, wsdlOperationMessage))
                    {
                        case MatchResult.None:
                            return MatchResult.None;
                        case MatchResult.Partial:
                            result = MatchResult.Partial;
                            break;
                    }
                }

                return result;
            }

            private static MatchResult MatchOperationParameterName(WsdlNS.MessageBinding wsdlMessageBinding, WsdlNS.OperationMessage wsdlOperationMessage)
            {
                string wsdlOperationMessageName = wsdlOperationMessage.Name;
                string wsdlMessageBindingName = wsdlMessageBinding.Name;

                if (wsdlOperationMessageName == wsdlMessageBindingName)
                {
                    return MatchResult.Exact;
                }

                string wsdlOperationMessageDecodedName = WsdlNamingHelper.GetOperationMessageName(wsdlOperationMessage).DecodedName;
                if ((wsdlOperationMessageName == null) && (wsdlMessageBindingName == wsdlOperationMessageDecodedName))
                {
                    return MatchResult.Partial;
                }
                else if ((wsdlMessageBindingName == null) && (wsdlOperationMessageName == wsdlOperationMessageDecodedName))
                {
                    return MatchResult.Partial;
                }
                else
                {
                    return MatchResult.None;
                }
            }

            private static OperationDescription FindOperationDescription(ContractDescription contract, WsdlNS.OperationBinding wsdlOperationBinding)
            {
                foreach (OperationDescription operationDescription in contract.Operations)
                {
                    if (CompareOperations(operationDescription, contract, wsdlOperationBinding))
                        return operationDescription;
                }

                throw DiagnosticUtility.ExceptionUtility.ThrowHelperError(new InvalidOperationException(string.Format(SRServiceModel.UnableToLocateOperation2, wsdlOperationBinding.Name, contract.Name)));
            }

            private static bool CompareOperations(OperationDescription operationDescription, ContractDescription parentContractDescription, WsdlNS.OperationBinding wsdlOperationBinding)
            {
                string wsdlOperationName = WsdlExporter.WsdlNamingHelper.GetWsdlOperationName(operationDescription, parentContractDescription);

                if (wsdlOperationName != wsdlOperationBinding.Name)
                    return false;

                if (operationDescription.Messages.Count > 2)
                    return false;

                // Either both have output message or neither have an output message;
                if (FindMessage(operationDescription.Messages, MessageDirection.Output) != (wsdlOperationBinding.Output != null))
                    return false;

                // Either both have output message or neither have an output message;
                if (FindMessage(operationDescription.Messages, MessageDirection.Input) != (wsdlOperationBinding.Input != null))
                    return false;

                return true;
            }

            private static bool FindMessage(MessageDescriptionCollection messageDescriptionCollection, MessageDirection transferDirection)
            {
                foreach (MessageDescription message in messageDescriptionCollection)
                    if (message.Direction == transferDirection)
                        return true;
                return false;
            }
        }

        internal static class WSAddressingHelper
        {
            internal static string GetWsaActionUri(WsdlNS.OperationMessage wsdlOperationMessage)
            {
                string actionUri = FindWsaActionAttribute(wsdlOperationMessage);
                return (actionUri == null) ? CreateDefaultWsaActionUri(wsdlOperationMessage) : actionUri;
            }

            internal static string FindWsaActionAttribute(WsdlNS.OperationMessage wsdlOperationMessage)
            {
                XmlAttribute[] attributes = wsdlOperationMessage.ExtensibleAttributes;
                if (attributes != null && attributes.Length > 0)
                {
                    foreach (XmlAttribute attribute in attributes)
                    {
                        if ((attribute.NamespaceURI == MetadataStrings.AddressingWsdl.NamespaceUri
                             || attribute.NamespaceURI == MetadataStrings.AddressingMetadata.NamespaceUri)
                            && attribute.LocalName == MetadataStrings.AddressingMetadata.Action)
                        {
                            return attribute.Value;
                        }
                    }
                }

                return null;
            }

            private static string CreateDefaultWsaActionUri(WsdlNS.OperationMessage wsdlOperationMessage)
            {
                if (wsdlOperationMessage is WsdlNS.OperationFault)
                    return AddressingVersion.WSAddressing10.DefaultFaultAction;

                // We figure out default action. All specifications' rules below are the same.
                // Using [WSDL Binding W3C Working Draft 13 April 2005] Section 3.3
                // Using [WSDL Binding W3C Candidate Recommendation 29 May 2006] Section 4.4.4
                // Using [Metadata W3C Working Draft 13 16 May 2007] Section 4.4.4

                string ns = wsdlOperationMessage.Operation.PortType.ServiceDescription.TargetNamespace ?? string.Empty;
                string portTypeName = wsdlOperationMessage.Operation.PortType.Name;
                XmlName operationMessageName = WsdlNamingHelper.GetOperationMessageName(wsdlOperationMessage);

                string delimiter = ns.StartsWith("urn:", StringComparison.OrdinalIgnoreCase) ? ":" : "/";

                string baseActionUri = ns.EndsWith(delimiter, StringComparison.OrdinalIgnoreCase) ? ns : ns + delimiter;

                string actionUri = string.Format(CultureInfo.InvariantCulture, "{0}{1}{2}{3}", baseActionUri, portTypeName, delimiter, operationMessageName.EncodedName);

                return actionUri;
            }

            internal static EndpointAddress ImportAddress(WsdlNS.Port wsdlPort)
            {
                //Try to read Endpoint Address from WsdlPort
                if (wsdlPort != null)
                {
                    XmlElement addressing10Element = wsdlPort.Extensions.Find(AddressingStrings.EndpointReference, Addressing10Strings.Namespace);
                    XmlElement addressing200408Element = wsdlPort.Extensions.Find(AddressingStrings.EndpointReference, Addressing200408Strings.Namespace);
                    WsdlNS.SoapAddressBinding soapAddressBinding = (WsdlNS.SoapAddressBinding)wsdlPort.Extensions.Find(typeof(WsdlNS.SoapAddressBinding));

                    //CONSIDER, hsomu: Maybe we should verify the version is consistent with teh binding here
                    if (addressing10Element != null)
                    {
                        return EndpointAddress.ReadFrom(AddressingVersion.WSAddressing10, new XmlNodeReader(addressing10Element));
                    }
                    if (addressing200408Element != null)
                    {
                        return EndpointAddress.ReadFrom(AddressingVersion.WSAddressingAugust2004, new XmlNodeReader(addressing200408Element));
                    }
                    else if (soapAddressBinding != null)
                    {
                        return new EndpointAddress(soapAddressBinding.Location);
                    }
                }
                return null;
            }

            internal static AddressingVersion FindAddressingVersion(PolicyConversionContext policyContext)
            {
                if (PolicyConversionContext.FindAssertion(policyContext.GetBindingAssertions(),
                            MetadataStrings.Addressing10.WsdlBindingPolicy.UsingAddressing,
                            MetadataStrings.Addressing10.WsdlBindingPolicy.NamespaceUri, true /*remove*/) != null)
                {
                    return AddressingVersion.WSAddressing10;
                }
                else if (PolicyConversionContext.FindAssertion(policyContext.GetBindingAssertions(),
                            MetadataStrings.Addressing10.MetadataPolicy.Addressing,
                            MetadataStrings.Addressing10.MetadataPolicy.NamespaceUri, true /*remove*/) != null)
                {
                    return AddressingVersion.WSAddressing10;
                }
                else if (PolicyConversionContext.FindAssertion(policyContext.GetBindingAssertions(),
                            MetadataStrings.Addressing200408.Policy.UsingAddressing,
                            MetadataStrings.Addressing200408.Policy.NamespaceUri, true /*remove*/) != null)
                {
                    return AddressingVersion.WSAddressingAugust2004;
                }
                else
                {
                    return AddressingVersion.None;
                }
            }

            internal static SupportedAddressingMode DetermineSupportedAddressingMode(MetadataImporter importer, PolicyConversionContext context)
            {
                // Do not remove this assertion - the message encoding binding element importer owns it.
                XmlElement addressingAssertion = PolicyConversionContext.FindAssertion(context.GetBindingAssertions(),
                    MetadataStrings.Addressing10.MetadataPolicy.Addressing, MetadataStrings.Addressing10.MetadataPolicy.NamespaceUri, false);

                if (addressingAssertion != null)
                {
                    XmlElement policyElement = null;
                    foreach (XmlNode node in addressingAssertion.ChildNodes)
                    {
                        if (node is XmlElement && MetadataSection.IsPolicyElement((XmlElement)node))
                        {
                            policyElement = (XmlElement)node;
                            break;
                        }
                    }

                    if (policyElement == null)
                    {
                        string message = string.Format(SRServiceModel.ElementRequired, MetadataStrings.Addressing10.MetadataPolicy.Prefix,
                            MetadataStrings.Addressing10.MetadataPolicy.Addressing, MetadataStrings.WSPolicy.Prefix,
                            MetadataStrings.WSPolicy.Elements.Policy);

                        importer.Errors.Add(new MetadataConversionError(message, false));
                        return SupportedAddressingMode.Anonymous;
                    }

                    IEnumerable<IEnumerable<XmlElement>> alternatives = importer.NormalizePolicy(new XmlElement[] { policyElement });
                    foreach (IEnumerable<XmlElement> alternative in alternatives)
                    {
                        foreach (XmlElement element in alternative)
                        {
                            if (element.NamespaceURI == MetadataStrings.Addressing10.MetadataPolicy.NamespaceUri)
                            {
                                if (element.LocalName == MetadataStrings.Addressing10.MetadataPolicy.NonAnonymousResponses)
                                {
                                    return SupportedAddressingMode.NonAnonymous;
                                }
                                else if (element.LocalName == MetadataStrings.Addressing10.MetadataPolicy.AnonymousResponses)
                                {
                                    return SupportedAddressingMode.Anonymous;
                                }
                            }
                        }
                    }
                }

                return SupportedAddressingMode.Anonymous;
            }
        }

        private static class WsdlNamingHelper
        {
            internal static XmlQualifiedName GetBindingName(WsdlNS.Binding wsdlBinding)
            {
                XmlName xmlName = new XmlName(wsdlBinding.Name, true /*isEncoded*/);
                return new XmlQualifiedName(xmlName.EncodedName, wsdlBinding.ServiceDescription.TargetNamespace);
            }

            internal static XmlQualifiedName GetBindingName(WsdlNS.Port wsdlPort)
            {
                // elenak: composing names have potential problem of generating name that looks like an encoded name, consider avoiding '_'
                XmlName xmlName = new XmlName(string.Format(CultureInfo.InvariantCulture, "{0}_{1}", wsdlPort.Service.Name, wsdlPort.Name), true /*isEncoded*/);
                return new XmlQualifiedName(xmlName.EncodedName, wsdlPort.Service.ServiceDescription.TargetNamespace);
            }

            internal static XmlName GetEndpointName(WsdlNS.Port wsdlPort)
            {
                return new XmlName(wsdlPort.Name, true /*isEncoded*/);
            }

            internal static XmlQualifiedName GetContractName(XmlQualifiedName wsdlPortTypeQName)
            {
                return wsdlPortTypeQName;
            }

            internal static string GetOperationName(WsdlNS.Operation wsdlOperation)
            {
                return wsdlOperation.Name;
            }

            internal static XmlName GetOperationMessageName(WsdlNS.OperationMessage wsdlOperationMessage)
            {
                string messageName = null;
                if (!string.IsNullOrEmpty(wsdlOperationMessage.Name))
                {
                    messageName = wsdlOperationMessage.Name;
                }
                else if (wsdlOperationMessage.Operation.Messages.Count == 1)
                {
                    messageName = wsdlOperationMessage.Operation.Name;
                }
                else if (wsdlOperationMessage.Operation.Messages.IndexOf(wsdlOperationMessage) == 0)
                {
                    if (wsdlOperationMessage is WsdlNS.OperationInput)
                        messageName = wsdlOperationMessage.Operation.Name + "Request";
                    else if (wsdlOperationMessage is WsdlNS.OperationOutput)
                        messageName = wsdlOperationMessage.Operation.Name + "Solicit";
                }
                else if (wsdlOperationMessage.Operation.Messages.IndexOf(wsdlOperationMessage) == 1)
                {
                    messageName = wsdlOperationMessage.Operation.Name + "Response";
                }
                else
                {
                    // elenak: why this is an Assert, and not an exception?
                    Fx.Assert("Unsupported WSDL OM (More than 2 OperationMessages encountered in an Operation or WsdlOM is invalid)");
                }
                // names the come from service description documents have to be valid NCNames; XmlName.ctor will validate.
                return new XmlName(messageName, true /*isEncoded*/);
            }
        }

        internal static class NetSessionHelper
        {
            internal static void SetInitiatingTerminating(OperationDescription operationDescription, WsdlNS.Operation wsdlOperation)
            {
                XmlAttribute isInitiating = FindAttribute(wsdlOperation.ExtensibleAttributes, WsdlExporter.NetSessionHelper.IsInitiating,
                    WsdlExporter.NetSessionHelper.NamespaceUri);

                if (isInitiating != null)
                {
                    if (isInitiating.Value == WsdlExporter.NetSessionHelper.True)
                    {
                        operationDescription.IsInitiating = true;
                    }
                    if (isInitiating.Value == WsdlExporter.NetSessionHelper.False)
                    {
                        operationDescription.IsInitiating = false;
                    }
                }

                XmlAttribute isTerminating = FindAttribute(wsdlOperation.ExtensibleAttributes, WsdlExporter.NetSessionHelper.IsTerminating,
                    WsdlExporter.NetSessionHelper.NamespaceUri);

                if (isTerminating != null)
                {
                    if (isTerminating.Value == WsdlExporter.NetSessionHelper.True)
                    {
                        operationDescription.IsTerminating = true;
                    }
                    if (isTerminating.Value == WsdlExporter.NetSessionHelper.False)
                    {
                        operationDescription.IsTerminating = false;
                    }
                }
            }

            internal static void SetSession(ContractDescription contractDescription, WsdlNS.PortType wsdlPortType)
            {
                XmlAttribute usingSession = FindAttribute(wsdlPortType.ExtensibleAttributes, WsdlExporter.NetSessionHelper.UsingSession,
                    WsdlExporter.NetSessionHelper.NamespaceUri);

                if (usingSession != null)
                {
                    if (usingSession.Value == WsdlExporter.NetSessionHelper.True)
                    {
                        contractDescription.SessionMode = SessionMode.Required;
                    }
                    if (usingSession.Value == WsdlExporter.NetSessionHelper.False)
                    {
                        contractDescription.SessionMode = SessionMode.NotAllowed;
                    }
                }
            }

            private static XmlAttribute FindAttribute(XmlAttribute[] attributes, string localName, string ns)
            {
                if (attributes != null)
                {
                    foreach (XmlAttribute attribute in attributes)
                    {
                        if (attribute.LocalName == localName && attribute.NamespaceURI == ns)
                        {
                            return attribute;
                        }
                    }
                }
                return null;
            }
        }

        internal static class SoapInPolicyWorkaroundHelper
        {
            public const string soapTransportUriKey = "TransportBindingElementImporter.TransportUri";
            private const string workaroundNS = NamingHelper.DefaultNamespace + "temporaryworkaround";
            private const string bindingAttrName = "bindingName";
            private const string bindingAttrNamespace = "bindingNamespace";
            private static XmlDocument s_xmlDocument;

            static public void InsertAdHocPolicy(WsdlNS.Binding wsdlBinding, string value, string key)
            {
                XmlQualifiedName wsdlBindingQName = new XmlQualifiedName(wsdlBinding.Name, wsdlBinding.ServiceDescription.TargetNamespace);
                string id = AddPolicyUri(wsdlBinding, key);
                InsertPolicy(key, id, wsdlBinding.ServiceDescription, value, wsdlBindingQName);
            }

            static public string FindAdHocTransportPolicy(PolicyConversionContext policyContext, out XmlQualifiedName wsdlBindingQName)
            {
                return FindAdHocPolicy(policyContext, soapTransportUriKey, out wsdlBindingQName);
            }

            static public string FindAdHocPolicy(PolicyConversionContext policyContext, string key, out XmlQualifiedName wsdlBindingQName)
            {
                if (policyContext == null)
                    throw DiagnosticUtility.ExceptionUtility.ThrowHelperArgumentNull("policyContext");

                XmlElement policy = PolicyConversionContext.FindAssertion(policyContext.GetBindingAssertions(), key, workaroundNS, true);
                if (policy != null)
                {
                    wsdlBindingQName = new XmlQualifiedName(policy.Attributes[bindingAttrName].Value, policy.Attributes[bindingAttrNamespace].Value);
                    return policy.InnerText;
                }
                else
                {
                    wsdlBindingQName = null;
                    return null;
                }
            }

            private static string AddPolicyUri(WsdlNS.Binding wsdlBinding, string name)
            {
                string policyUris = ReadPolicyUris(wsdlBinding);
                string id = string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0}_{1}_BindingAdHocPolicy", wsdlBinding.Name, name);
                string newPolicyUris = string.Format(System.Globalization.CultureInfo.InvariantCulture, "#{0} {1}", id, policyUris).Trim();
                WritePolicyUris(wsdlBinding, newPolicyUris);
                return id;
            }

            private static XmlDocument XmlDoc
            {
                get
                {
                    if (s_xmlDocument == null)
                    {
                        NameTable nameTable = new NameTable();
                        nameTable.Add(MetadataStrings.WSPolicy.Elements.Policy);
                        nameTable.Add(MetadataStrings.WSPolicy.Elements.All);
                        nameTable.Add(MetadataStrings.WSPolicy.Elements.ExactlyOne);
                        nameTable.Add(MetadataStrings.WSPolicy.Attributes.PolicyURIs);
                        nameTable.Add(MetadataStrings.Wsu.Attributes.Id);
                        s_xmlDocument = new XmlDocument(nameTable);
                    }
                    return s_xmlDocument;
                }
            }

            private static void WritePolicyUris(WsdlNS.DocumentableItem item, string newValue)
            {
                int i;
                XmlAttribute[] attributes = item.ExtensibleAttributes;
                if (attributes != null && attributes.Length > 0)
                {
                    foreach (XmlAttribute attribute in attributes)
                        if (MetadataImporter.PolicyHelper.IsPolicyURIs(attribute))
                        {
                            attribute.Value = newValue;
                            return;
                        }
                    // Need to extend
                    i = attributes.Length;
                    Array.Resize<XmlAttribute>(ref attributes, i + 1);
                }
                else
                {
                    //Need to create
                    i = 0;
                    attributes = new XmlAttribute[1];
                }

                attributes[i] = CreatePolicyURIsAttribute(newValue);
                item.ExtensibleAttributes = attributes;
            }

            private static XmlAttribute CreatePolicyURIsAttribute(string value)
            {
                XmlAttribute attribute = XmlDoc.CreateAttribute(MetadataStrings.WSPolicy.Prefix,
                                                            MetadataStrings.WSPolicy.Attributes.PolicyURIs,
                                                            MetadataStrings.WSPolicy.NamespaceUri);

                attribute.Value = value;
                return attribute;
            }

            private static string ReadPolicyUris(WsdlNS.DocumentableItem item)
            {
                XmlAttribute[] attributes = item.ExtensibleAttributes;
                if (attributes != null && attributes.Length > 0)
                {
                    foreach (XmlAttribute attribute in attributes)
                    {
                        if (MetadataImporter.PolicyHelper.IsPolicyURIs(attribute))
                        {
                            return attribute.Value;
                        }
                    }
                }

                return string.Empty;
            }

            private static void InsertPolicy(string key, string id, WsdlNS.ServiceDescription policyWsdl, string value, XmlQualifiedName wsdlBindingQName)
            {
                // Create [wsp:Policy]
                XmlElement policyElement = CreatePolicyElement(key, value, wsdlBindingQName);

                //Create [wsp:Policy/@wsu:Id]
                XmlAttribute idAttribute = XmlDoc.CreateAttribute(MetadataStrings.Wsu.Prefix,
                                                            MetadataStrings.Wsu.Attributes.Id,
                                                            MetadataStrings.Wsu.NamespaceUri);
                idAttribute.Value = id;
                policyElement.SetAttributeNode(idAttribute);

                // Add wsp:Policy To WSDL
                policyWsdl.Extensions.Add(policyElement);
            }

            private static XmlElement CreatePolicyElement(string elementName, string value, XmlQualifiedName wsdlBindingQName)
            {
                // Create [wsp:Policy]
                XmlElement policyElement = XmlDoc.CreateElement(MetadataStrings.WSPolicy.Prefix,
                                                            MetadataStrings.WSPolicy.Elements.Policy,
                                                            MetadataStrings.WSPolicy.NamespaceUri);

                // Create [wsp:Policy/wsp:ExactlyOne]
                XmlElement exactlyOneElement = XmlDoc.CreateElement(MetadataStrings.WSPolicy.Prefix,
                                                            MetadataStrings.WSPolicy.Elements.ExactlyOne,
                                                            MetadataStrings.WSPolicy.NamespaceUri);
                policyElement.AppendChild(exactlyOneElement);

                // Create [wsp:Policy/wsp:ExactlyOne/wsp:All]
                XmlElement allElement = XmlDoc.CreateElement(MetadataStrings.WSPolicy.Prefix,
                                                            MetadataStrings.WSPolicy.Elements.All,
                                                            MetadataStrings.WSPolicy.NamespaceUri);
                exactlyOneElement.AppendChild(allElement);

                // Add [wsp:Policy/wsp:ExactlyOne/wsp:All/*]
                XmlElement workaroundElement = s_xmlDocument.CreateElement(elementName, workaroundNS);
                workaroundElement.InnerText = value;

                XmlAttribute bindingName = s_xmlDocument.CreateAttribute(bindingAttrName);
                bindingName.Value = wsdlBindingQName.Name;
                workaroundElement.Attributes.Append(bindingName);

                XmlAttribute bindingNamespace = s_xmlDocument.CreateAttribute(bindingAttrNamespace);
                bindingNamespace.Value = wsdlBindingQName.Namespace;
                workaroundElement.Attributes.Append(bindingNamespace);

                workaroundElement.Attributes.Append(bindingNamespace);


                allElement.AppendChild(workaroundElement);

                return policyElement;
            }

            internal static void InsertAdHocTransportPolicy(WsdlNS.ServiceDescriptionCollection wsdlDocuments)
            {
                foreach (WsdlNS.ServiceDescription wsdl in wsdlDocuments)
                    if (wsdl != null)
                    {
                        foreach (WsdlNS.Binding wsdlBinding in wsdl.Bindings)
                        {
                            if (WsdlImporter.WsdlPolicyReader.ContainsPolicy(wsdlBinding))
                            {
                                WsdlNS.SoapBinding soapBinding = (WsdlNS.SoapBinding)wsdlBinding.Extensions.Find(typeof(WsdlNS.SoapBinding));
                                if (soapBinding != null)
                                    WsdlImporter
                                        .SoapInPolicyWorkaroundHelper
                                        .InsertAdHocPolicy(wsdlBinding, soapBinding.Transport, soapTransportUriKey);
                            }
                        }
                    }
            }
        }

        private IEnumerable<WsdlNS.Binding> FindBindingsForPortType(WsdlNS.PortType wsdlPortType)
        {
            foreach (WsdlNS.Binding wsdlBinding in GetAllBindings())
            {
                if (wsdlBinding.Type.Name == wsdlPortType.Name
                    && wsdlBinding.Type.Namespace == wsdlPortType.ServiceDescription.TargetNamespace)
                    yield return wsdlBinding;
            }
        }

        private IEnumerable<WsdlNS.Binding> FindBindingsForContract(ContractDescription contract)
        {
            XmlQualifiedName qName = WsdlExporter.WsdlNamingHelper.GetPortTypeQName(contract);
            foreach (WsdlNS.Binding wsdlBinding in GetAllBindings())
            {
                if (wsdlBinding.Type.Name == qName.Name
                    && wsdlBinding.Type.Namespace == qName.Namespace)
                    yield return wsdlBinding;
            }
        }

        private IEnumerable<WsdlNS.Port> FindPortsForBinding(WsdlNS.Binding binding)
        {
            foreach (WsdlNS.Port wsdlPort in GetAllPorts())
            {
                if (wsdlPort.Binding.Name == binding.Name && wsdlPort.Binding.Namespace == binding.ServiceDescription.TargetNamespace)
                    yield return wsdlPort;
            }
        }

        private IEnumerable<WsdlNS.Binding> GetAllBindings()
        {
            foreach (WsdlNS.ServiceDescription wsdl in this.WsdlDocuments)
            {
                foreach (WsdlNS.Binding wsdlBinding in wsdl.Bindings)
                {
                    yield return wsdlBinding;
                }
            }
        }

        private IEnumerable<WsdlNS.Port> GetAllPorts()
        {
            foreach (WsdlNS.ServiceDescription wsdl in this.WsdlDocuments)
            {
                foreach (WsdlNS.Service wsdlService in wsdl.Services)
                {
                    foreach (WsdlNS.Port wsdlPort in wsdlService.Ports)
                    {
                        yield return wsdlPort;
                    }
                }
            }
        }

        // TODO :[Fx.Tag.SecurityNote(Critical = "Uses ClientSection.UnsafeGetSection to get config in PT.",
        //    Safe = "Does not leak config object, just picks up extensions.")]
        [SecuritySafeCritical]
        private static Collection<IWsdlImportExtension> LoadWsdlExtensionsFromConfig()
        {
            // implement extension laoding with code w/o going to the config file.
            //throw new NotImplementedException();
            Collection<IWsdlImportExtension> extensions = new Collection<IWsdlImportExtension>
            {
                new System.ServiceModel.Description.DataContractSerializerMessageContractImporter(),
                new System.ServiceModel.Description.XmlSerializerMessageContractImporter(),
                new System.ServiceModel.Channels.MessageEncodingBindingElementImporter(),
                new System.ServiceModel.Channels.TransportBindingElementImporter(),
                new System.ServiceModel.Channels.StandardBindingImporter(),
                new System.ServiceModel.Channels.UdpTransportImporter(),
                new System.ServiceModel.Channels.ContextBindingElementImporter()
            };

            return extensions;
        }

        internal static IEnumerable<MetadataSection> CreateMetadataDocuments(WsdlNS.ServiceDescriptionCollection wsdlDocuments, XmlSchemaSet xmlSchemas, IEnumerable<XmlElement> policyDocuments)
        {
            if (wsdlDocuments != null)
                foreach (WsdlNS.ServiceDescription wsdl in wsdlDocuments)
                    yield return MetadataSection.CreateFromServiceDescription(wsdl);

            if (xmlSchemas != null)
                foreach (XmlSchema schema in xmlSchemas.Schemas())
                    yield return MetadataSection.CreateFromSchema(schema);

            if (policyDocuments != null)
                foreach (XmlElement policyDocument in policyDocuments)
                    yield return MetadataSection.CreateFromPolicy(policyDocument, null);
        }

        private BindingElementCollection ImportPolicyFromWsdl(WsdlEndpointConversionContext endpointContext)
        {
            PolicyAlternatives policyAlternatives = this.PolicyReader.GetPolicyAlternatives(endpointContext);
            IEnumerable<PolicyConversionContext> policyContexts = GetPolicyConversionContextEnumerator(endpointContext.Endpoint, policyAlternatives, this.Quotas);
            PolicyConversionContext firstContext = null;

            StringBuilder unImportedPolicyMessage = null;
            int policyConversionContextsSeen = 0; //limit on the number of alternatives that we'll evaluate
            foreach (PolicyConversionContext policyConversionContext in policyContexts)
            {
                if (firstContext == null)
                    firstContext = policyConversionContext;
                if (this.TryImportPolicy(policyConversionContext))
                {
                    return policyConversionContext.BindingElements;
                }
                else
                {
                    AppendUnImportedPolicyErrorMessage(ref unImportedPolicyMessage, endpointContext, policyConversionContext);
                }

                if (++policyConversionContextsSeen >= this.Quotas.MaxPolicyConversionContexts)
                {
                    break;
                }
            }

            // we failed to import policy for all PolicyConversionContexts, lets pick one context (the first one)
            // and wrap all unprocessed assertion in UnrecognizedAssertionsBindingElement
            if (firstContext != null)
            {
#pragma warning disable 56506
                firstContext.BindingElements.Insert(0, CollectUnrecognizedAssertions(firstContext, endpointContext));
                LogImportWarning(unImportedPolicyMessage.ToString());
                return firstContext.BindingElements;
            }
            // Consider: a /verbose option for svcutil...
            if (endpointContext.WsdlPort != null)
            {
                throw DiagnosticUtility.ExceptionUtility.ThrowHelperError(WsdlImportException.Create(endpointContext.WsdlPort, new InvalidOperationException(SRServiceModel.NoUsablePolicyAssertions)));
            }
            else
            {
                throw DiagnosticUtility.ExceptionUtility.ThrowHelperError(WsdlImportException.Create(endpointContext.WsdlBinding, new InvalidOperationException(SRServiceModel.NoUsablePolicyAssertions)));
            }
        }

        private static UnrecognizedAssertionsBindingElement CollectUnrecognizedAssertions(PolicyConversionContext policyContext, WsdlEndpointConversionContext endpointContext)
        {
            XmlQualifiedName bindingQName = new XmlQualifiedName(endpointContext.WsdlBinding.Name, endpointContext.WsdlBinding.ServiceDescription.TargetNamespace);
            UnrecognizedAssertionsBindingElement unknownBindingElement = new UnrecognizedAssertionsBindingElement(bindingQName, policyContext.GetBindingAssertions());
            foreach (OperationDescription operation in policyContext.Contract.Operations)
            {
                if (policyContext.GetOperationBindingAssertions(operation).Count != 0)
                {
                    unknownBindingElement.Add(operation, policyContext.GetOperationBindingAssertions(operation));
                }

                foreach (MessageDescription message in operation.Messages)
                {
                    if (policyContext.GetMessageBindingAssertions(message).Count != 0)
                    {
                        unknownBindingElement.Add(message, policyContext.GetMessageBindingAssertions(message));
                    }
                }
            }
            return unknownBindingElement;
        }

        private static void AppendUnImportedPolicyErrorMessage(ref StringBuilder unImportedPolicyMessage, WsdlEndpointConversionContext endpointContext, PolicyConversionContext policyContext)
        {
            if (unImportedPolicyMessage == null)
            {
                unImportedPolicyMessage = new StringBuilder(SRServiceModel.UnabletoImportPolicy);
            }
            else
            {
                unImportedPolicyMessage.AppendLine();
            }

            if (policyContext.GetBindingAssertions().Count != 0)
                AddUnImportedPolicyString(unImportedPolicyMessage, endpointContext.WsdlBinding, policyContext.GetBindingAssertions());

            foreach (OperationDescription operation in policyContext.Contract.Operations)
            {
                if (policyContext.GetOperationBindingAssertions(operation).Count != 0)
                {
                    AddUnImportedPolicyString(unImportedPolicyMessage,
                        endpointContext.GetOperationBinding(operation),
                        policyContext.GetOperationBindingAssertions(operation));
                }

                foreach (MessageDescription message in operation.Messages)
                {
                    if (policyContext.GetMessageBindingAssertions(message).Count != 0)
                    {
                        AddUnImportedPolicyString(unImportedPolicyMessage,
                            endpointContext.GetMessageBinding(message),
                            policyContext.GetMessageBindingAssertions(message));
                    }
                }
            }
        }

        private static void AddUnImportedPolicyString(StringBuilder stringBuilder, WsdlNS.NamedItem item, IEnumerable<XmlElement> unimportdPolicy)
        {
            stringBuilder.AppendLine(string.Format(SRServiceModel.UnImportedAssertionList, CreateXPathString(item)));
            // do not putput duplicated assetions
            Dictionary<XmlElement, XmlElement> unique = new Dictionary<XmlElement, XmlElement>();
            int uniqueAsserions = 0;
            foreach (XmlElement element in unimportdPolicy)
            {
                if (unique.ContainsKey(element))
                    continue;
                unique.Add(element, element);
                uniqueAsserions++;
                if (uniqueAsserions > 128)
                {
                    stringBuilder.Append("..");
                    stringBuilder.AppendLine();
                    break;
                }
                WriteElement(element, stringBuilder);
            }
        }

        private static void WriteElement(XmlElement element, StringBuilder stringBuilder)
        {
            stringBuilder.Append("    <");
            stringBuilder.Append(element.Name);
            if (!string.IsNullOrEmpty(element.NamespaceURI))
            {
                stringBuilder.Append(' ');
                stringBuilder.Append("xmlns");
                if (!string.IsNullOrEmpty(element.Prefix))
                {
                    stringBuilder.Append(':');
                    stringBuilder.Append(element.Prefix);
                }
                stringBuilder.Append('=');
                stringBuilder.Append('\'');
                stringBuilder.Append(element.NamespaceURI);
                stringBuilder.Append('\'');
            }
            stringBuilder.Append(">..</");
            stringBuilder.Append(element.Name);
            stringBuilder.Append('>');
            stringBuilder.AppendLine();
        }

        private const string xPathDocumentFormatString = "//wsdl:definitions[@targetNamespace='{0}']";
        private const string xPathItemSubFormatString = "/wsdl:{0}";
        private const string xPathNamedItemSubFormatString = xPathItemSubFormatString + "[@name='{1}']";

        private static string GetElementName(WsdlNS.NamedItem item)
        {
            if (item is WsdlNS.PortType)
                return "wsdl:portType";
            else if (item is WsdlNS.Binding)
                return "wsdl:binding";
            else if (item is WsdlNS.ServiceDescription)
                return "wsdl:definitions";
            else if (item is WsdlNS.Service)
                return "wsdl:service";
            else if (item is WsdlNS.Message)
                return "wsdl:message";
            else if (item is WsdlNS.Operation)
                return "wsdl:operation";
            else if (item is WsdlNS.Port)
                return "wsdl:port";
            else
            {
                Fx.Assert("GetElementName Method should be updated to support " + item.GetType());
                return null;
            }
        }

        private static string CreateXPathString(WsdlNS.NamedItem item)
        {
            if (item == null)
                return SRServiceModel.XPathUnavailable;
            string nameValue = item.Name;
            string localName;
            string wsdlNs;
            string rest = string.Empty;
            string itemPath = string.Empty;

            GetXPathParameters(item, out wsdlNs, out localName, ref nameValue, ref rest);
            string documentPath = string.Format(CultureInfo.InvariantCulture, xPathDocumentFormatString, wsdlNs);

            if (localName != null)
            {
                itemPath = string.Format(CultureInfo.InvariantCulture, xPathNamedItemSubFormatString, localName, nameValue);
            }
            Fx.Assert(rest != null, "GetXPathParameters Method should never set rest to null. this happened for: " + item.GetType());
            return documentPath + itemPath + rest;
        }

        private static void GetXPathParameters(WsdlNS.NamedItem item, out string wsdlNs, out string localName, ref string nameValue, ref string rest)
        {
            if (item is WsdlNS.ServiceDescription)
            {
                localName = null;
                wsdlNs = ((WsdlNS.ServiceDescription)item).TargetNamespace ?? String.Empty;
            }
            if (item is WsdlNS.PortType)
            {
                localName = "portType";
                wsdlNs = ((WsdlNS.PortType)item).ServiceDescription.TargetNamespace ?? String.Empty;
            }
            else if (item is WsdlNS.Binding)
            {
                localName = "binding";
                wsdlNs = ((WsdlNS.Binding)item).ServiceDescription.TargetNamespace ?? String.Empty;
            }
            else if (item is WsdlNS.ServiceDescription)
            {
                localName = "definitions";
                wsdlNs = ((WsdlNS.ServiceDescription)item).TargetNamespace ?? String.Empty;
            }
            else if (item is WsdlNS.Service)
            {
                localName = "service";
                wsdlNs = ((WsdlNS.Service)item).ServiceDescription.TargetNamespace ?? String.Empty;
            }
            else if (item is WsdlNS.Message)
            {
                localName = "message";
                wsdlNs = ((WsdlNS.Message)item).ServiceDescription.TargetNamespace ?? String.Empty;
            }
            else if (item is WsdlNS.Port)
            {
                WsdlNS.Service wsdlService = ((WsdlNS.Port)item).Service;
                localName = "service";
                nameValue = wsdlService.Name;
                wsdlNs = wsdlService.ServiceDescription.TargetNamespace ?? String.Empty;
                rest = string.Format(CultureInfo.InvariantCulture, xPathNamedItemSubFormatString, "port", item.Name);
            }
            else if (item is WsdlNS.Operation)
            {
                WsdlNS.PortType wsdlPortType = ((WsdlNS.Operation)item).PortType;
                localName = "portType";
                nameValue = wsdlPortType.Name;
                wsdlNs = wsdlPortType.ServiceDescription.TargetNamespace ?? String.Empty;
                rest = string.Format(CultureInfo.InvariantCulture, xPathNamedItemSubFormatString, "operation", item.Name);
            }
            else if (item is WsdlNS.OperationBinding)
            {
                WsdlNS.OperationBinding wsdlOperationBinding = ((WsdlNS.OperationBinding)item);
                localName = "binding";
                nameValue = wsdlOperationBinding.Binding.Name;
                wsdlNs = wsdlOperationBinding.Binding.ServiceDescription.TargetNamespace ?? String.Empty;
                rest = string.Format(CultureInfo.InvariantCulture, xPathNamedItemSubFormatString, "operation", item.Name);
            }
            else if (item is WsdlNS.MessageBinding)
            {
                localName = "binding";
                WsdlNS.OperationBinding wsdlOperationBinding = ((WsdlNS.MessageBinding)item).OperationBinding;
                wsdlNs = wsdlOperationBinding.Binding.ServiceDescription.TargetNamespace ?? String.Empty;
                nameValue = wsdlOperationBinding.Binding.Name;
                string messageName = item.Name;

                string messageTag = string.Empty;
                if (item is WsdlNS.InputBinding)
                    messageTag = "input";
                else if (item is WsdlNS.OutputBinding)
                    messageTag = "output";
                else if (item is WsdlNS.FaultBinding)
                    messageTag = "fault";
                else
                    Fx.Assert("Unsupported WSDL OM: unknown WsdlNS.MessageBinding: " + item.GetType());

                rest = string.Format(CultureInfo.InvariantCulture, xPathNamedItemSubFormatString, "operation", wsdlOperationBinding.Name);
                if (string.IsNullOrEmpty(messageName))
                    rest += string.Format(CultureInfo.InvariantCulture, xPathItemSubFormatString, messageTag);
                else
                    rest += string.Format(CultureInfo.InvariantCulture, xPathNamedItemSubFormatString, messageTag, messageName);
            }
            else
            {
                Fx.Assert("GetXPathParameters Method should be updated to support " + item.GetType());
                localName = null;
                wsdlNs = null;
            }
        }

        private void LogImportWarning(string warningMessage)
        {
            if (_warnings.ContainsKey(warningMessage))
                return;
            if (_warnings.Count >= 1024)
                _warnings.Clear();
            _warnings.Add(warningMessage, warningMessage);
            this.Errors.Add(new MetadataConversionError(warningMessage, true));
        }

        private void LogImportError(WsdlNS.NamedItem item, WsdlImportException wie, bool isWarning)
        {
            string errormessage;
            if (wie.InnerException != null && (wie.InnerException is WsdlImportException))
            {
                WsdlImportException wieInner = wie.InnerException as WsdlImportException;
                string dependencyMessage = string.Format(SRServiceModel.WsdlImportErrorDependencyDetail, GetElementName(wieInner.SourceItem), GetElementName(item), CreateXPathString(wieInner.SourceItem));
                errormessage = string.Format(SRServiceModel.WsdlImportErrorMessageDetail, GetElementName(item), CreateXPathString(wie.SourceItem), dependencyMessage);
            }
            else
            {
                errormessage = string.Format(SRServiceModel.WsdlImportErrorMessageDetail, GetElementName(item), CreateXPathString(wie.SourceItem), wie.Message);
            }

            _importErrors.Add(item, wie);
            this.Errors.Add(new MetadataConversionError(errormessage, isWarning));
        }

        private static Exception CreateBeforeImportExtensionException(IWsdlImportExtension importer, Exception e)
        {
            string errorMessage = string.Format(SRServiceModel.WsdlExtensionBeforeImportError, importer.GetType().AssemblyQualifiedName, e.Message);
            return new InvalidOperationException(errorMessage, e);
        }

        private Exception CreateAlreadyFaultedException(WsdlNS.NamedItem item)
        {
            WsdlImportException innerException = _importErrors[item];
            string warningMsg = string.Format(SRServiceModel.WsdlItemAlreadyFaulted, GetElementName(item));
            return new AlreadyFaultedException(warningMsg, innerException);
        }

        private static Exception CreateExtensionException(IWsdlImportExtension importer, Exception e)
        {
            string errorMessage = string.Format(SRServiceModel.WsdlExtensionImportError, importer.GetType().FullName, e.Message);
            //consider hsomu, allow internal exceptions to throw WsdlImportException and handle it in some special way?
            return new InvalidOperationException(errorMessage, e);
        }

        private class AlreadyFaultedException : InvalidOperationException
        {
            internal AlreadyFaultedException(string message, WsdlImportException innerException)
                : base(message, innerException)
            { }
        }

        private class WsdlImportException : Exception
        {
            private WsdlNS.NamedItem _sourceItem;
            private readonly string _xPath = null;

            private WsdlImportException(WsdlNS.NamedItem item, Exception innerException)
                : base(string.Empty, innerException)
            {
                _xPath = CreateXPathString(item);
                _sourceItem = item;
            }

            internal static WsdlImportException Create(WsdlNS.NamedItem item, Exception innerException)
            {
                WsdlImportException wie = innerException as WsdlImportException;
                if (wie != null && wie.IsChildNodeOf(item))
                {
                    wie._sourceItem = item;
                    return wie;
                }
                else
                {
                    AlreadyFaultedException afe = innerException as AlreadyFaultedException;
                    if (afe != null)
                        return new WsdlImportException(item, afe.InnerException);
                    else
                        return new WsdlImportException(item, innerException);
                }
            }

            internal bool IsChildNodeOf(WsdlNS.NamedItem item)
            {
                return this.XPath.StartsWith(CreateXPathString(item), StringComparison.Ordinal);
            }

            internal string XPath { get { return _xPath; } }

            internal WsdlNS.NamedItem SourceItem { get { return _sourceItem; } }

            public override string Message
            {
                get
                {
                    Exception messageException = this.InnerException;

                    while (messageException is WsdlImportException)
                        messageException = messageException.InnerException;

                    if (messageException == null)
                        return string.Empty;

                    return messageException.Message;
                }
            }
        }

        internal class WsdlPolicyReader
        {
            private WsdlImporter _importer;
            private WsdlPolicyDictionary _policyDictionary;

            private static readonly string[] s_emptyStringArray = new string[0];

            internal WsdlPolicyReader(WsdlImporter importer)
            {
                _importer = importer;
                _policyDictionary = new WsdlPolicyDictionary(importer);
                importer.PolicyWarningOccured += this.LogPolicyNormalizationWarning;
            }

            private IEnumerable<IEnumerable<XmlElement>> GetPolicyAlternatives(WsdlNS.NamedItem item, WsdlNS.ServiceDescription wsdl)
            {
                Collection<XmlElement> policyElements = new Collection<XmlElement>();

                foreach (XmlElement element in GetReferencedPolicy(item, wsdl))
                {
                    policyElements.Add(element);
                }

                foreach (XmlElement element in GetEmbeddedPolicy(item))
                {
                    policyElements.Add(element);
                    if (!_policyDictionary.PolicySourceTable.ContainsKey(element))
                        _policyDictionary.PolicySourceTable.Add(element, wsdl);
                }

                return _importer.NormalizePolicy(policyElements);
            }

            private void LogPolicyNormalizationWarning(XmlElement contextAssertion, string warningMessage)
            {
                string xPath = null;
                if (contextAssertion != null)
                    xPath = _policyDictionary.CreateIdXPath(contextAssertion);

                StringBuilder warningMsg = new StringBuilder();
                warningMsg.AppendLine(warningMessage);

                if (!string.IsNullOrEmpty(xPath))
                {
                    warningMsg.AppendLine(string.Format(SRServiceModel.XPathPointer, xPath));
                }
                else
                {
                    //
                    // We were given a context assertion that we couldn't get an XPath for
                    //
                    warningMsg.AppendLine(string.Format(SRServiceModel.XPathPointer, SRServiceModel.XPathUnavailable));
                }
                _importer.LogImportWarning(warningMsg.ToString());
            }

            internal static bool ContainsPolicy(WsdlNS.Binding wsdlBinding)
            {
                if (HasPolicyAttached(wsdlBinding))
                    return true;

                foreach (WsdlNS.OperationBinding wsdlOperationBinding in wsdlBinding.Operations)
                {
                    if (HasPolicyAttached(wsdlOperationBinding))
                    {
                        return true;
                    }
                    if (wsdlOperationBinding.Input != null && HasPolicyAttached(wsdlOperationBinding.Input))
                    {
                        return true;
                    }
                    if (wsdlOperationBinding.Output != null && HasPolicyAttached(wsdlOperationBinding.Output))
                    {
                        return true;
                    }
                    foreach (WsdlNS.FaultBinding faultBinding in wsdlOperationBinding.Faults)
                    {
                        if (HasPolicyAttached(faultBinding))
                        {
                            return true;
                        }
                    }
                }

                return false;
            }

            internal static bool HasPolicy(WsdlNS.Port wsdlPort)
            {
                return HasPolicyAttached(wsdlPort);
            }

            internal static IEnumerable<XmlElement> GetEmbeddedPolicy(WsdlNS.NamedItem item)
            {
                List<XmlElement> embeddedPolicies = new List<XmlElement>();
                embeddedPolicies.AddRange(item.Extensions.FindAll(MetadataStrings.WSPolicy.Elements.Policy,
                    MetadataStrings.WSPolicy.NamespaceUri));
                embeddedPolicies.AddRange(item.Extensions.FindAll(MetadataStrings.WSPolicy.Elements.Policy,
                    MetadataStrings.WSPolicy.NamespaceUri15));
                return embeddedPolicies;
            }

            private IEnumerable<XmlElement> GetReferencedPolicy(WsdlNS.NamedItem item, WsdlNS.ServiceDescription wsdl)
            {
                string xPath = CreateXPathString(item);

                foreach (string policyRef in GetPolicyReferenceUris(item, xPath))
                {
                    XmlElement policy = _policyDictionary.ResolvePolicyReference(policyRef, wsdl);
                    if (policy == null)
                    {
                        StringBuilder warningMsg = new StringBuilder();
                        warningMsg.AppendLine(string.Format(SRServiceModel.UnableToFindPolicyWithId, policyRef));
                        warningMsg.AppendLine(string.Format(SRServiceModel.XPathPointer, xPath));
                        _importer.LogImportWarning(warningMsg.ToString());
                        continue;
                    }
                    yield return policy;
                }
            }

            private IEnumerable<string> GetPolicyReferenceUris(WsdlNS.NamedItem item, string xPath)
            {
                //
                // get policy from wsp:PolicyUris attribute
                //
                foreach (string policyUri in ReadPolicyUrisAttribute(item))
                    yield return policyUri;

                //
                // get policy from <wsp:PolicyReference> Elements
                //
                foreach (string policyUri in ReadPolicyReferenceElements(item, xPath))
                    yield return policyUri;
            }

            private IEnumerable<string> ReadPolicyReferenceElements(WsdlNS.NamedItem item, string xPath)
            {
                List<XmlElement> policyReferences = new List<XmlElement>();
                policyReferences.AddRange(item.Extensions.FindAll(MetadataStrings.WSPolicy.Elements.PolicyReference,
                    MetadataStrings.WSPolicy.NamespaceUri));
                policyReferences.AddRange(item.Extensions.FindAll(MetadataStrings.WSPolicy.Elements.PolicyReference,
                    MetadataStrings.WSPolicy.NamespaceUri15));

                foreach (XmlElement element in policyReferences)
                {
                    string idRef = element.GetAttribute(MetadataStrings.WSPolicy.Attributes.URI);

                    if (idRef == null)
                    {
                        string warningMsg = string.Format(SRServiceModel.PolicyReferenceMissingURI, MetadataStrings.WSPolicy.Attributes.URI);
                        _importer.LogImportWarning(warningMsg);
                    }
                    else if (idRef == string.Empty)
                    {
                        string warningMsg = SRServiceModel.PolicyReferenceInvalidId;
                        _importer.LogImportWarning(warningMsg);
                        continue;
                    }
                    else
                    {
                        yield return idRef;
                    }
                }
            }

            private static string[] ReadPolicyUrisAttribute(WsdlNS.NamedItem item)
            {
                XmlAttribute[] attributes = item.ExtensibleAttributes;
                if (attributes != null && attributes.Length > 0)
                {
                    foreach (XmlAttribute attribute in attributes)
                    {
                        if (PolicyHelper.IsPolicyURIs(attribute))
                        {
                            return attribute.Value.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
                        }
                    }
                }

                return s_emptyStringArray;
            }

            private static bool HasPolicyAttached(WsdlNS.NamedItem item)
            {
                XmlAttribute[] attributes = item.ExtensibleAttributes;
                if (attributes != null && Array.Exists(attributes, PolicyHelper.IsPolicyURIs))
                {
                    return true;
                }

                if (item.Extensions.Find(MetadataStrings.WSPolicy.Elements.PolicyReference, MetadataStrings.WSPolicy.NamespaceUri) != null
                    || item.Extensions.Find(MetadataStrings.WSPolicy.Elements.PolicyReference, MetadataStrings.WSPolicy.NamespaceUri15) != null)
                {
                    return true;
                }

                if (item.Extensions.Find(MetadataStrings.WSPolicy.Elements.Policy, MetadataStrings.WSPolicy.NamespaceUri) != null
                    || item.Extensions.Find(MetadataStrings.WSPolicy.Elements.Policy, MetadataStrings.WSPolicy.NamespaceUri15) != null)
                {
                    return true;
                }

                return false;
            }

            internal PolicyAlternatives GetPolicyAlternatives(WsdlEndpointConversionContext endpointContext)
            {
                PolicyAlternatives policyAlternatives = new PolicyAlternatives();

                //
                // Create EndpointAlternatives either from wsd:binding or from CrossProduct of wsd:binding and wsdl:port policy
                //
                WsdlNS.ServiceDescription bindingWsdl = endpointContext.WsdlBinding.ServiceDescription;
                IEnumerable<IEnumerable<XmlElement>> wsdlBindingAlternatives = this.GetPolicyAlternatives(endpointContext.WsdlBinding, bindingWsdl);
                if (endpointContext.WsdlPort != null)
                {
                    IEnumerable<IEnumerable<XmlElement>> wsdlPortAlternatives = this.GetPolicyAlternatives(endpointContext.WsdlPort, endpointContext.WsdlPort.Service.ServiceDescription);
                    policyAlternatives.EndpointAlternatives = PolicyHelper.CrossProduct<XmlElement>(wsdlBindingAlternatives, wsdlPortAlternatives, new YieldLimiter(_importer.Quotas.MaxYields, _importer));
                }
                else
                {
                    policyAlternatives.EndpointAlternatives = wsdlBindingAlternatives;
                }

                //
                // Create operation and message policy
                //
                policyAlternatives.OperationBindingAlternatives = new Dictionary<OperationDescription, IEnumerable<IEnumerable<XmlElement>>>(endpointContext.Endpoint.Contract.Operations.Count);
                policyAlternatives.MessageBindingAlternatives = new Dictionary<MessageDescription, IEnumerable<IEnumerable<XmlElement>>>();
                policyAlternatives.FaultBindingAlternatives = new Dictionary<FaultDescription, IEnumerable<IEnumerable<XmlElement>>>();

                foreach (OperationDescription operation in endpointContext.Endpoint.Contract.Operations)
                {
                    // Skip operations that have Action/ReplyAction = "*" 
                    if (!WsdlExporter.OperationIsExportable(operation))
                    {
                        continue;
                    }

                    WsdlNS.OperationBinding wsdlOperationBinding = endpointContext.GetOperationBinding(operation);
                    try
                    {
                        IEnumerable<IEnumerable<XmlElement>> operationAlternatives = this.GetPolicyAlternatives(wsdlOperationBinding, bindingWsdl);
                        policyAlternatives.OperationBindingAlternatives.Add(operation, operationAlternatives);

                        foreach (MessageDescription message in operation.Messages)
                        {
                            WsdlNS.MessageBinding wsdlMessageBinding = endpointContext.GetMessageBinding(message);
                            CreateMessageBindingAlternatives(policyAlternatives, bindingWsdl, message, wsdlMessageBinding);
                        }

                        foreach (FaultDescription fault in operation.Faults)
                        {
                            WsdlNS.FaultBinding wsdlFaultBinding = endpointContext.GetFaultBinding(fault);
                            CreateFaultBindingAlternatives(policyAlternatives, bindingWsdl, fault, wsdlFaultBinding);
                        }
                    }
#pragma warning disable 56500 // covered by FxCOP
                    catch (Exception e)
                    {
                        if (Fx.IsFatal(e))
                            throw;
                        throw DiagnosticUtility.ExceptionUtility.ThrowHelperError(WsdlImportException.Create(wsdlOperationBinding, e));
                    }
                }

                return policyAlternatives;
            }

            private void CreateMessageBindingAlternatives(PolicyAlternatives policyAlternatives, WsdlNS.ServiceDescription bindingWsdl, MessageDescription message, WsdlNS.MessageBinding wsdlMessageBinding)
            {
                try
                {
                    IEnumerable<IEnumerable<XmlElement>> messageAlternatives = this.GetPolicyAlternatives(wsdlMessageBinding, bindingWsdl);
                    policyAlternatives.MessageBindingAlternatives.Add(message, messageAlternatives);
                }
#pragma warning disable 56500 // covered by FxCOP
                catch (Exception e)
                {
                    if (Fx.IsFatal(e))
                        throw;
                    throw DiagnosticUtility.ExceptionUtility.ThrowHelperError(WsdlImportException.Create(wsdlMessageBinding, e));
                }
            }

            private void CreateFaultBindingAlternatives(PolicyAlternatives policyAlternatives, WsdlNS.ServiceDescription bindingWsdl, FaultDescription fault, WsdlNS.FaultBinding wsdlFaultBinding)
            {
                try
                {
                    IEnumerable<IEnumerable<XmlElement>> faultAlternatives = this.GetPolicyAlternatives(wsdlFaultBinding, bindingWsdl);
                    policyAlternatives.FaultBindingAlternatives.Add(fault, faultAlternatives);
                }
#pragma warning disable 56500 // covered by FxCOP
                catch (Exception e)
                {
                    if (Fx.IsFatal(e))
                        throw;
                    throw DiagnosticUtility.ExceptionUtility.ThrowHelperError(WsdlImportException.Create(wsdlFaultBinding, e));
                }
            }

            internal XmlElement ResolvePolicyReference(string policyReference, XmlElement contextPolicyAssertion)
            {
                return _policyDictionary.ResolvePolicyReference(policyReference, contextPolicyAssertion);
            }

            private class WsdlPolicyDictionary
            {
                private readonly MetadataImporter _importer;
                private readonly Dictionary<WsdlNS.ServiceDescription, Dictionary<string, XmlElement>> _embeddedPolicyDictionary = new Dictionary<WsdlNS.ServiceDescription, Dictionary<string, XmlElement>>();
                private readonly Dictionary<string, XmlElement> _externalPolicyDictionary = new Dictionary<string, XmlElement>();
                private readonly Dictionary<XmlElement, WsdlNS.ServiceDescription> _policySourceTable = new Dictionary<XmlElement, WsdlNS.ServiceDescription>();

                internal Dictionary<XmlElement, WsdlNS.ServiceDescription> PolicySourceTable
                {
                    get { return _policySourceTable; }
                }

                internal WsdlPolicyDictionary(WsdlImporter importer)
                {
                    _importer = importer;

                    //
                    // Embedded Policy documents
                    //
                    foreach (WsdlNS.ServiceDescription wsdl in importer._wsdlDocuments)
                    {
                        foreach (XmlElement element in WsdlPolicyReader.GetEmbeddedPolicy(wsdl))
                        {
                            AddEmbeddedPolicy(importer, wsdl, element);
                        }
                    }

                    //
                    // External Policy documents
                    //
                    foreach (KeyValuePair<string, XmlElement> policyDocument in importer._policyDocuments)
                    {
                        AddExternalPolicy(importer, policyDocument);
                    }
                }

                private void AddEmbeddedPolicy(WsdlImporter importer, WsdlNS.ServiceDescription wsdl, XmlElement element)
                {
                    string key = GetFragmentIdentifier(element);
                    if (String.IsNullOrEmpty(key))
                    {
                        string xPath = CreateXPathString(wsdl);
                        string warningMsg = string.Format(SRServiceModel.PolicyInWsdlMustHaveFragmentId, xPath);
                        importer.LogImportWarning(warningMsg);
                        return;
                    }

                    Dictionary<string, XmlElement> wsdlPolicyDictionary;
                    if (!_embeddedPolicyDictionary.TryGetValue(wsdl, out wsdlPolicyDictionary))
                    {
                        wsdlPolicyDictionary = new Dictionary<string, XmlElement>();
                        _embeddedPolicyDictionary.Add(wsdl, wsdlPolicyDictionary);
                    }
                    else if (wsdlPolicyDictionary.ContainsKey(key))
                    {
                        string xPath = CreateIdXPath(wsdl, element, key);
                        string warningMsg = string.Format(SRServiceModel.DuplicatePolicyInWsdlSkipped, xPath);
                        importer.LogImportWarning(warningMsg);
                        return;
                    }

                    wsdlPolicyDictionary.Add(key, element);
                    _policySourceTable.Add(element, wsdl);
                }

                private void AddExternalPolicy(WsdlImporter importer, KeyValuePair<string, XmlElement> policyDocument)
                {
                    if (policyDocument.Value.NamespaceURI != MetadataStrings.WSPolicy.NamespaceUri
                        && policyDocument.Value.NamespaceURI != MetadataStrings.WSPolicy.NamespaceUri15)
                    {
                        string warningMsg = string.Format(SRServiceModel.UnrecognizedPolicyDocumentNamespace, policyDocument.Value.NamespaceURI);
                        importer.LogImportWarning(warningMsg);
                        return;
                    }

                    if (PolicyHelper.GetNodeType(policyDocument.Value) != PolicyHelper.NodeType.Policy)
                    {
                        string warningMsg = string.Format(SRServiceModel.UnsupportedPolicyDocumentRoot, policyDocument.Value.Name);
                        importer.LogImportWarning(warningMsg);
                        return;
                    }

                    string key = CreateKeyFromPolicy(policyDocument.Key, policyDocument.Value);
                    if (_externalPolicyDictionary.ContainsKey(key))
                    {
                        string warningMsg = string.Format(SRServiceModel.DuplicatePolicyDocumentSkipped, key);
                        importer.LogImportWarning(warningMsg);
                        return;
                    }

                    _externalPolicyDictionary.Add(key, policyDocument.Value);
                }

                internal XmlElement ResolvePolicyReference(string policyReference, XmlElement contextPolicyAssertion)
                {
                    XmlElement policy;

                    if (policyReference[0] != '#')
                    {
                        _externalPolicyDictionary.TryGetValue(policyReference, out policy);
                        return policy;
                    }

                    if (contextPolicyAssertion == null)
                    {
                        return null;
                    }

                    WsdlNS.ServiceDescription sourceWsdl;
                    if (!_policySourceTable.TryGetValue(contextPolicyAssertion, out sourceWsdl))
                    {
                        return null;
                    }

                    return ResolvePolicyReference(policyReference, sourceWsdl);
                }

                internal XmlElement ResolvePolicyReference(string policyReference, WsdlNS.ServiceDescription wsdlDocument)
                {
                    XmlElement policy;
                    if (policyReference[0] != '#')
                    {
                        _externalPolicyDictionary.TryGetValue(policyReference, out policy);
                        return policy;
                    }

                    Dictionary<string, XmlElement> wsdlPolicyDictionary;
                    if (!_embeddedPolicyDictionary.TryGetValue(wsdlDocument, out wsdlPolicyDictionary))
                    {
                        return null;
                    }

                    wsdlPolicyDictionary.TryGetValue(policyReference, out policy);
                    return policy;
                }

                private static string CreateKeyFromPolicy(string identifier, XmlElement policyElement)
                {
                    string policyId = GetFragmentIdentifier(policyElement);
                    string policyUri = string.Format(CultureInfo.InvariantCulture, "{0}{1}", identifier, policyId);
                    return policyUri;
                }

                private static string GetFragmentIdentifier(XmlElement element)
                {
                    return PolicyHelper.GetFragmentIdentifier(element);
                }

                private static readonly string s_wspPolicy = string.Format(CultureInfo.InvariantCulture, "{0}:{1}", MetadataStrings.WSPolicy.Prefix, MetadataStrings.WSPolicy.Elements.Policy);
                private static readonly string s_xmlId = string.Format(CultureInfo.InvariantCulture, "{0}:{1}", MetadataStrings.Xml.Prefix, MetadataStrings.Xml.Attributes.Id);
                private static readonly string s_wsuId = string.Format(CultureInfo.InvariantCulture, "{0}:{1}", MetadataStrings.Wsu.Prefix, MetadataStrings.Wsu.Attributes.Id);

                internal string CreateIdXPath(XmlElement policyAssertion)
                {
                    WsdlNS.ServiceDescription sourceWsdl;
                    if (!_policySourceTable.TryGetValue(policyAssertion, out sourceWsdl))
                    {
                        return null;
                    }
                    string key = GetFragmentIdentifier(policyAssertion);
                    if (string.IsNullOrEmpty(key))
                    {
                        return null;
                    }
                    return CreateIdXPath(sourceWsdl, policyAssertion, key);
                }

                internal static string CreateIdXPath(WsdlNS.ServiceDescription wsdl, XmlElement element, string key)
                {
                    string xPath = CreateXPathString(wsdl);

                    string idAttrib;
                    if (element.HasAttribute(MetadataStrings.Wsu.Attributes.Id, MetadataStrings.Wsu.NamespaceUri))
                    {
                        idAttrib = s_wsuId;
                    }
                    else if (element.HasAttribute(MetadataStrings.Xml.Attributes.Id, MetadataStrings.Xml.NamespaceUri))
                    {
                        idAttrib = s_xmlId;
                    }
                    else
                    {
                        Fx.Assert("CreateIdXPath always called with a valid key");
                        return null;
                    }
                    return string.Format(CultureInfo.InvariantCulture, "{0}/{1}/[@{2}='{3}']", xPath, s_wspPolicy, idAttrib, key);
                }
            }
        }

        private enum ErrorBehavior
        {
            RethrowExceptions,
            DoNotThrowExceptions,
        }

        private enum WsdlPortTypeImportOptions
        {
            ReuseExistingContracts,
            IgnoreExistingContracts,
        }
    }
}
