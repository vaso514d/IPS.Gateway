# Readability inventory

Base: 077470b. All 232 current authored production C# files are listed, with deleted/replaced source paths retained for traceability. Each file was checked in its feature increment or the completion audit; direct implementations were retained where they already followed the chosen conventions. Generated files and Contracts are excluded.

| File | Review status |
|---|---|
| src/IPS.Middleware.Domain/AggregateRoot.cs | 005a reviewed |
| src/IPS.Middleware.Domain/Inbound/IncomingPayment.cs | 005a reviewed |
| src/IPS.Middleware.Domain/Inbound/IncomingProcessing.cs | 005a reviewed |
| src/IPS.Middleware.Domain/Inbound/IncomingReconciliationRecorded.cs | 005a reviewed |
| src/IPS.Middleware.Domain/Transactions/OutgoingPayment.cs | 005a reviewed |
| src/IPS.Middleware.Domain/Transactions/PaymentEvents.cs | 005a reviewed |
| src/IPS.Middleware.Domain/Transactions/TransactionLifecycle.cs | 005a reviewed |
| src/IPS.Middleware.Application/Abstractions/Payments/IIpsReplyInterpreter.cs | 005c reviewed |
| src/IPS.Middleware.Application/Abstractions/Payments/IIpsTransport.cs | 005c reviewed |
| src/IPS.Middleware.Application/Abstractions/Payments/IOutgoingPaymentRepository.cs | 005c reviewed |
| src/IPS.Middleware.Application/Abstractions/Payments/IPacs008MessagePreparation.cs | 005c reviewed |
| src/IPS.Middleware.Application/Abstractions/Payments/IPaymentPreparationRepository.cs | 005c reviewed |
| src/IPS.Middleware.Application/Abstractions/Payments/IPaymentSubmissionRepository.cs | 005c reviewed |
| src/IPS.Middleware.Application/Abstractions/Payments/ITransactionWorkRepository.cs | 005c reviewed |
| src/IPS.Middleware.Application/Abstractions/Persistence/IUnitOfWork.cs | 005f audited; existing direct implementation retained |
| src/IPS.Middleware.Application/Abstractions/Persistence/PersistenceConcurrencyException.cs | 005f audited; existing direct implementation retained |
| src/IPS.Middleware.Application/Abstractions/Persistence/UniqueConstraintException.cs | 005f audited; existing direct implementation retained |
| src/IPS.Middleware.Application/Inbound/Composition/IIncomingCompositionRepository.cs | 005d reviewed |
| src/IPS.Middleware.Application/Inbound/Composition/IIncomingWorkflowExecution.cs | 005d reviewed |
| src/IPS.Middleware.Application/Inbound/Composition/IncomingComposition.cs | 005d reviewed |
| src/IPS.Middleware.Application/Inbound/Composition/IncomingCompositionOptions.cs | 005d reviewed |
| src/IPS.Middleware.Application/Inbound/Composition/IncomingCompositionResult.cs | 005d reviewed |
| src/IPS.Middleware.Application/Inbound/Composition/IncomingReceiptPreparation.cs | 005d reviewed |
| src/IPS.Middleware.Application/Inbound/Composition/IncomingReceiptState.cs | 005d reviewed |
| src/IPS.Middleware.Application/Inbound/Pacs008/IncomingPacs008.cs | 005d reviewed |
| src/IPS.Middleware.Application/Inbound/Processing/CoreCall.cs | 005d reviewed |
| src/IPS.Middleware.Application/Inbound/Processing/CoreCallExecution.cs | 005d reviewed |
| src/IPS.Middleware.Application/Inbound/Processing/IIncomingCoreClient.cs | 005d reviewed |
| src/IPS.Middleware.Application/Inbound/Processing/IIncomingCoreReplyInterpreter.cs | 005d reviewed |
| src/IPS.Middleware.Application/Inbound/Processing/IIncomingProcessingRepository.cs | 005d reviewed |
| src/IPS.Middleware.Application/Inbound/Processing/IncomingPacs008Processing.cs | 005d reviewed |
| src/IPS.Middleware.Application/Inbound/Processing/IncomingProcessingOptions.cs | 005d reviewed |
| src/IPS.Middleware.Application/Inbound/Processing/IncomingProcessingSnapshot.cs | 005d reviewed |
| src/IPS.Middleware.Application/Inbound/Processing/ProcessingBudget.cs | 005d reviewed |
| src/IPS.Middleware.Application/Inbound/Receipts/IInboundReceiptRepository.cs | 005d reviewed |
| src/IPS.Middleware.Application/Inbound/Receipts/IInboundWorkRepository.cs | 005d reviewed |
| src/IPS.Middleware.Application/Inbound/Receipts/IIncomingReceiveClient.cs | 005d reviewed |
| src/IPS.Middleware.Application/Inbound/Receipts/InboundReceipt.cs | 005d reviewed |
| src/IPS.Middleware.Application/Inbound/Receipts/InboundReceiptIntake.cs | 005d reviewed |
| src/IPS.Middleware.Application/Inbound/Receipts/InboundWork.cs | 005d reviewed |
| src/IPS.Middleware.Application/Inbound/Reconciliation/IIncomingReconciliationRepository.cs | 005d reviewed |
| src/IPS.Middleware.Application/Inbound/Reconciliation/IIncomingReversalClient.cs | 005d reviewed |
| src/IPS.Middleware.Application/Inbound/Reconciliation/IncomingReconciliation.cs | 005d reviewed |
| src/IPS.Middleware.Application/Inbound/Reconciliation/IncomingReconciliationOptions.cs | 005d reviewed |
| src/IPS.Middleware.Application/Inbound/Reconciliation/ReversalNotification.cs | 005d reviewed |
| src/IPS.Middleware.Application/Inbound/Registration/IIncomingPaymentRepository.cs | 005d reviewed |
| src/IPS.Middleware.Application/Inbound/Registration/IIncomingPaymentWorkRepository.cs | 005d reviewed |
| src/IPS.Middleware.Application/Inbound/Registration/IncomingPaymentIntake.cs | 005d reviewed |
| src/IPS.Middleware.Application/Inbound/Registration/IncomingPaymentWork.cs | 005d reviewed |
| src/IPS.Middleware.Application/Inbound/Registration/IncomingRegistration.cs | 005d reviewed |
| src/IPS.Middleware.Application/Inbound/Replies/IIncomingReplyClient.cs | 005d reviewed |
| src/IPS.Middleware.Application/Inbound/Replies/IIncomingReplyProtocol.cs | 005d reviewed |
| src/IPS.Middleware.Application/Inbound/Replies/IIncomingReplyRepository.cs | 005d reviewed |
| src/IPS.Middleware.Application/Inbound/Replies/IncomingReply.cs | 005d reviewed |
| src/IPS.Middleware.Application/Inbound/Replies/IncomingReplyOptions.cs | 005d reviewed |
| src/IPS.Middleware.Application/Inbound/Replies/IncomingReplyProcessing.cs | 005d reviewed |
| src/IPS.Middleware.Application/Payments/Execution/OutgoingExecutionOptions.cs | 005c reviewed |
| src/IPS.Middleware.Application/Payments/Execution/OutgoingSubmission.cs | 005c reviewed |
| src/IPS.Middleware.Application/Payments/Investigation/IInvestigationProtocol.cs | 005c reviewed |
| src/IPS.Middleware.Application/Payments/Investigation/IInvestigationRepository.cs | 005c reviewed |
| src/IPS.Middleware.Application/Payments/Investigation/InvestigationAttempt.cs | 005c reviewed |
| src/IPS.Middleware.Application/Payments/Investigation/InvestigationOptions.cs | 005c reviewed |
| src/IPS.Middleware.Application/Payments/Investigation/InvestigationReply.cs | 005c reviewed |
| src/IPS.Middleware.Application/Payments/Investigation/OutgoingInvestigation.cs | 005c reviewed |
| src/IPS.Middleware.Application/Payments/Pacs008/AcceptedPacs008.cs | 005c reviewed |
| src/IPS.Middleware.Application/Payments/Pacs008/IpsReply.cs | 005c reviewed |
| src/IPS.Middleware.Application/Payments/Pacs008/IpsSubmissionResponse.cs | 005c reviewed |
| src/IPS.Middleware.Application/Payments/Pacs008/OutgoingMessage.cs | 005c reviewed |
| src/IPS.Middleware.Application/Payments/Pacs008/Pacs008Intake.cs | 005c reviewed |
| src/IPS.Middleware.Application/Payments/Pacs008/Pacs008Options.cs | 005c reviewed |
| src/IPS.Middleware.Application/Payments/Pacs008/Pacs008Policy.cs | 005c reviewed |
| src/IPS.Middleware.Application/Payments/Pacs008/Pacs008Processing.cs | 005c reviewed |
| src/IPS.Middleware.Application/Payments/Pacs008/Pacs008ProtocolProfile.cs | 005c reviewed |
| src/IPS.Middleware.Application/Payments/Pacs008/Pacs008Request.cs | 005c reviewed |
| src/IPS.Middleware.Application/Payments/Pacs008/Pacs008Text.cs | 005c reviewed |
| src/IPS.Middleware.Application/Payments/Pacs008/PaymentDetails.cs | 005c reviewed |
| src/IPS.Middleware.Application/Payments/Pacs008/PaymentSubmission.cs | 005c reviewed |
| src/IPS.Middleware.Application/Payments/Pacs008/PreparedPaymentMessage.cs | 005c reviewed |
| src/IPS.Middleware.Application/Payments/Pacs008/ValidatedPacs008.cs | 005c reviewed |
| src/IPS.Middleware.Application/Payments/Pacs008/Validation/Pacs008Validator.cs | 005c reviewed |
| src/IPS.Middleware.Application/Payments/Pacs008/Validation/PartyValidators.cs | 005c reviewed |
| src/IPS.Middleware.Application/Payments/Pacs008/Validation/PaymentChecksums.cs | 005c reviewed |
| src/IPS.Middleware.Application/Payments/Pacs008/Validation/ProtocolTextRules.cs | 005c reviewed |
| src/IPS.Middleware.Application/Payments/Pacs008/Validation/RemittanceValidators.cs | 005c reviewed |
| src/IPS.Middleware.Application/Payments/PaymentMessageTypes.cs | 005c reviewed |
| src/IPS.Middleware.Application/Payments/StatusDelivery/IOutgoingStatusReceiver.cs | 005c reviewed |
| src/IPS.Middleware.Application/Payments/StatusDelivery/IOutgoingStatusRepository.cs | 005c reviewed |
| src/IPS.Middleware.Application/Payments/StatusDelivery/OutgoingStatus.cs | 005c reviewed |
| src/IPS.Middleware.Application/Payments/StatusDelivery/OutgoingStatusDelivery.cs | 005c reviewed |
| src/IPS.Middleware.Application/Payments/StatusDelivery/OutgoingStatusQuery.cs | 005c reviewed |
| src/IPS.Middleware.Application/Payments/StatusDelivery/OutgoingStatusReader.cs | 005c reviewed |
| src/IPS.Middleware.Application/Payments/StatusDelivery/StatusDeliveryOptions.cs | 005c reviewed |
| src/IPS.Middleware.Application/Transactions/IntakeValidationResult.cs | 005c reviewed |
| src/IPS.Middleware.Application/Transactions/OutgoingTransactionIntake.cs | 005c reviewed |
| src/IPS.Middleware.Application/Transactions/OutgoingTransactionWork.cs | 005c reviewed |
| src/IPS.Middleware.Application/Transactions/StoredPaymentEvent.cs | 005c reviewed |
| src/IPS.Middleware.Application/Transactions/TransactionClaim.cs | 005c reviewed |
| src/IPS.Middleware.Application/Transactions/TransactionIntakeResult.cs | 005c reviewed |
| src/IPS.Middleware.Application/Transactions/TransactionWorkResult.cs | 005c reviewed |
| src/IPS.Middleware.Application/Transactions/ValidatedIntakeRequest.cs | 005c reviewed |
| src/IPS.Middleware.Infrastructure/Inbound/FreshScopeRetry.cs | 005f audited; existing direct implementation retained |
| src/IPS.Middleware.Infrastructure/Inbound/InboundJournalChannel.cs | 005f audited; existing direct implementation retained |
| src/IPS.Middleware.Infrastructure/Inbound/InboundProcessingChannel.cs | 005f audited; existing direct implementation retained |
| src/IPS.Middleware.Infrastructure/Inbound/InboundReceiptRegistration.cs | 005f audited; existing direct implementation retained |
| src/IPS.Middleware.Infrastructure/Inbound/InboundReplyChannel.cs | 005f audited; existing direct implementation retained |
| src/IPS.Middleware.Infrastructure/Inbound/InboundSchedulingOptions.cs | 005f audited; existing direct implementation retained |
| src/IPS.Middleware.Infrastructure/Inbound/InboundServices.cs | 005f audited; existing direct implementation retained |
| src/IPS.Middleware.Infrastructure/Inbound/InboundWorkDiscovery.cs | 005f audited; existing direct implementation retained |
| src/IPS.Middleware.Infrastructure/Inbound/IncomingCompositionServices.cs | 005f audited; existing direct implementation retained |
| src/IPS.Middleware.Infrastructure/Inbound/IncomingPaymentRegistration.cs | 005f audited; existing direct implementation retained |
| src/IPS.Middleware.Infrastructure/Inbound/IncomingWorkflowExecution.cs | 005f audited; existing direct implementation retained |
| src/IPS.Middleware.Infrastructure/Inbound/Pacs008/IncomingCoreReplyInterpreter.cs | 005d reviewed |
| src/IPS.Middleware.Infrastructure/Inbound/Pacs008/IncomingPacs002Reply.cs | 005d reviewed |
| src/IPS.Middleware.Infrastructure/Inbound/Pacs008/IncomingPacs008CoreMapping.cs | 005d reviewed |
| src/IPS.Middleware.Infrastructure/Inbound/Pacs008/IncomingPacs008Mapping.cs | 005d reviewed |
| src/IPS.Middleware.Infrastructure/Inbound/Pacs008/IncomingPacs008Reader.cs | 005d reviewed |
| src/IPS.Middleware.Infrastructure/Inbound/Pacs008/IncomingReplyProtocol.cs | 005d reviewed |
| src/IPS.Middleware.Infrastructure/Inbound/Pacs008/IncomingReversalContract.cs | 005d reviewed |
| src/IPS.Middleware.Infrastructure/Inbound/Transport/IncomingHttpClients.cs | 005e replaced by one file per client |
| src/IPS.Middleware.Infrastructure/Inbound/Transport/IncomingHttpRegistration.cs | 005e reviewed |
| src/IPS.Middleware.Infrastructure/Inbound/Transport/IncomingTransportCertificates.cs | 005e reviewed |
| src/IPS.Middleware.Infrastructure/Inbound/Transport/IncomingTransportSettings.cs | 005e reviewed |
| src/IPS.Middleware.Infrastructure/Inbound/Workers/InboundDispatchDiscovery.cs | 005e reviewed |
| src/IPS.Middleware.Infrastructure/Inbound/Workers/IncomingFollowUpWorker.cs | 005e reviewed |
| src/IPS.Middleware.Infrastructure/Inbound/Workers/IncomingProcessingWorker.cs | 005e reviewed |
| src/IPS.Middleware.Infrastructure/Inbound/Workers/IncomingReceiveWorker.cs | 005e reviewed |
| src/IPS.Middleware.Infrastructure/Inbound/Workers/IncomingReplyAdmission.cs | 005e reviewed |
| src/IPS.Middleware.Infrastructure/Inbound/Workers/IncomingReplyWorker.cs | 005e reviewed |
| src/IPS.Middleware.Infrastructure/Inbound/Workers/IncomingWorker.cs | 005e reviewed |
| src/IPS.Middleware.Infrastructure/Inbound/Workers/IncomingWorkerOptions.cs | 005e reviewed |
| src/IPS.Middleware.Infrastructure/Inbound/Workers/IncomingWorkerServices.cs | 005e reviewed |
| src/IPS.Middleware.Infrastructure/Payments/Execution/OutgoingRuntime.cs | 005e reviewed |
| src/IPS.Middleware.Infrastructure/Payments/Execution/SupervisedWork.cs | 005e reviewed |
| src/IPS.Middleware.Infrastructure/Payments/Investigation/InvestigationExecution.cs | 005c reviewed |
| src/IPS.Middleware.Infrastructure/Payments/Investigation/InvestigationProtocol.cs | 005c reviewed |
| src/IPS.Middleware.Infrastructure/Payments/Investigation/InvestigationRegistration.cs | 005c reviewed |
| src/IPS.Middleware.Infrastructure/Payments/Investigation/Pacs028ReplyInterpreter.cs | 005c reviewed |
| src/IPS.Middleware.Infrastructure/Payments/Investigation/Pacs028Xml.cs | 005c reviewed |
| src/IPS.Middleware.Infrastructure/Payments/Pacs008/IpsReplyInterpreter.cs | 005c reviewed |
| src/IPS.Middleware.Infrastructure/Payments/Pacs008/Pacs008Message.cs | 005c reviewed |
| src/IPS.Middleware.Infrastructure/Payments/Pacs008/Pacs008Preparation.cs | 005c reviewed |
| src/IPS.Middleware.Infrastructure/Payments/Pacs008/Pacs008Schema.cs | 005c reviewed |
| src/IPS.Middleware.Infrastructure/Payments/Pacs008/Pacs008Xml.cs | 005c reviewed |
| src/IPS.Middleware.Infrastructure/Payments/Pacs008/PaymentMessageContext.cs | 005c reviewed |
| src/IPS.Middleware.Infrastructure/Payments/Pacs008/Signing/IpsSignatureVerifier.cs | 005c reviewed |
| src/IPS.Middleware.Infrastructure/Payments/Pacs008/Signing/IpsSignatureXml.cs | 005c reviewed |
| src/IPS.Middleware.Infrastructure/Payments/Pacs008/Signing/ISigningCertificateSource.cs | 005c reviewed |
| src/IPS.Middleware.Infrastructure/Payments/Pacs008/Signing/MessageSigning.cs | 005c reviewed |
| src/IPS.Middleware.Infrastructure/Payments/Pacs008/Signing/Pacs008MessageSigner.cs | 005c reviewed |
| src/IPS.Middleware.Infrastructure/Payments/Pacs008/Signing/Pacs008SigningPolicy.cs | 005c reviewed |
| src/IPS.Middleware.Infrastructure/Payments/Pacs008/Signing/SignedInfoCanonicalization.cs | 005c reviewed |
| src/IPS.Middleware.Infrastructure/Payments/StatusDelivery/OutgoingStatusContract.cs | 005c reviewed |
| src/IPS.Middleware.Infrastructure/Payments/Transport/OutgoingHttpClients.cs | 005e replaced by one file per client |
| src/IPS.Middleware.Infrastructure/Payments/Transport/OutgoingHttpRegistration.cs | 005e reviewed |
| src/IPS.Middleware.Infrastructure/Payments/Transport/OutgoingTransportCertificates.cs | 005e reviewed |
| src/IPS.Middleware.Infrastructure/Payments/Transport/OutgoingTransportSettings.cs | 005e reviewed |
| src/IPS.Middleware.Infrastructure/Persistence/Configurations/AggregateIdentityConfiguration.cs | 005b reviewed |
| src/IPS.Middleware.Infrastructure/Persistence/Configurations/InboundJournalConfiguration.cs | 005b reviewed |
| src/IPS.Middleware.Infrastructure/Persistence/Configurations/IncomingCoreCallConfiguration.cs | 005b reviewed |
| src/IPS.Middleware.Infrastructure/Persistence/Configurations/IncomingPaymentConfiguration.cs | 005b reviewed |
| src/IPS.Middleware.Infrastructure/Persistence/Configurations/IncomingReplyConfiguration.cs | 005b reviewed |
| src/IPS.Middleware.Infrastructure/Persistence/Configurations/InvestigationConfiguration.cs | 005b reviewed |
| src/IPS.Middleware.Infrastructure/Persistence/Configurations/OutgoingMessageConfiguration.cs | 005b reviewed |
| src/IPS.Middleware.Infrastructure/Persistence/Configurations/OutgoingPaymentConfiguration.cs | 005b reviewed |
| src/IPS.Middleware.Infrastructure/Persistence/Configurations/OutgoingStatusDeliveryConfiguration.cs | 005b reviewed |
| src/IPS.Middleware.Infrastructure/Persistence/Configurations/TransactionEventConfiguration.cs | 005b reviewed |
| src/IPS.Middleware.Infrastructure/Persistence/DesignTimeTransactionDbContextFactory.cs | 005b reviewed |
| src/IPS.Middleware.Infrastructure/Persistence/Events/AggregateIdentity.cs | 005b reviewed |
| src/IPS.Middleware.Infrastructure/Persistence/Events/TransactionEventRow.cs | 005b reviewed |
| src/IPS.Middleware.Infrastructure/Persistence/Inbound/InboundJournalEntry.cs | 005b reviewed |
| src/IPS.Middleware.Infrastructure/Persistence/Inbound/IncomingCoreCallRow.cs | 005b reviewed |
| src/IPS.Middleware.Infrastructure/Persistence/Inbound/IncomingPaymentColumns.cs | 005b replaced by typed metadata |
| src/IPS.Middleware.Infrastructure/Persistence/Inbound/IncomingPaymentJson.cs | 005b reviewed |
| src/IPS.Middleware.Infrastructure/Persistence/Inbound/IncomingReplyRow.cs | 005b reviewed |
| src/IPS.Middleware.Infrastructure/Persistence/Interceptors/DomainEventsInterceptor.cs | 005b reviewed |
| src/IPS.Middleware.Infrastructure/Persistence/Interceptors/InboundPersistenceInterceptor.cs | 005b reviewed |
| src/IPS.Middleware.Infrastructure/Persistence/Interceptors/InvestigationInterceptor.cs | 005b reviewed |
| src/IPS.Middleware.Infrastructure/Persistence/Interceptors/OutgoingJournalInterceptor.cs | 005b reviewed |
| src/IPS.Middleware.Infrastructure/Persistence/Interceptors/OutgoingStatusDeliveryInterceptor.cs | 005b reviewed |
| src/IPS.Middleware.Infrastructure/Persistence/Interceptors/PaymentPersistenceInterceptor.cs | 005b reviewed |
| src/IPS.Middleware.Infrastructure/Persistence/Interceptors/SaveRuleInterceptor.cs | 005b reviewed |
| src/IPS.Middleware.Infrastructure/Persistence/Outgoing/InvestigationRow.cs | 005b reviewed |
| src/IPS.Middleware.Infrastructure/Persistence/Outgoing/OutgoingJournal.cs | 005b reviewed |
| src/IPS.Middleware.Infrastructure/Persistence/Outgoing/OutgoingMessageRow.cs | 005b reviewed |
| src/IPS.Middleware.Infrastructure/Persistence/Outgoing/OutgoingStatusDeliveryRow.cs | 005b reviewed |
| src/IPS.Middleware.Infrastructure/Persistence/Outgoing/OutgoingStatusProjection.cs | 005b reviewed |
| src/IPS.Middleware.Infrastructure/Persistence/PaymentArtifacts.cs | 005b reviewed |
| src/IPS.Middleware.Infrastructure/Persistence/PaymentColumns.cs | 005b reviewed |
| src/IPS.Middleware.Infrastructure/Persistence/PaymentJson.cs | 005b reviewed |
| src/IPS.Middleware.Infrastructure/Persistence/PersistenceRegistration.cs | 005b reviewed |
| src/IPS.Middleware.Infrastructure/Persistence/TransactionDbContext.cs | 005b reviewed |
| src/IPS.Middleware.Infrastructure/Properties/AssemblyInfo.cs | 005f audited; existing direct implementation retained |
| src/IPS.Middleware.Infrastructure/Repositories/Inbound/InboundReceiptRepository.cs | 005b reviewed |
| src/IPS.Middleware.Infrastructure/Repositories/Inbound/InboundWorkRepository.cs | 005b reviewed |
| src/IPS.Middleware.Infrastructure/Repositories/Inbound/IncomingCompositionRepository.cs | 005b reviewed |
| src/IPS.Middleware.Infrastructure/Repositories/Inbound/IncomingPaymentRepository.cs | 005b reviewed |
| src/IPS.Middleware.Infrastructure/Repositories/Inbound/IncomingPaymentWorkRepository.cs | 005b reviewed |
| src/IPS.Middleware.Infrastructure/Repositories/Inbound/IncomingProcessingRepository.cs | 005b reviewed |
| src/IPS.Middleware.Infrastructure/Repositories/Inbound/IncomingReconciliationRepository.cs | 005b reviewed |
| src/IPS.Middleware.Infrastructure/Repositories/Inbound/IncomingReplyRepository.cs | 005b reviewed |
| src/IPS.Middleware.Infrastructure/Repositories/Payments/InvestigationRepository.cs | 005b reviewed |
| src/IPS.Middleware.Infrastructure/Repositories/Payments/OutgoingPaymentRepository.cs | 005b reviewed |
| src/IPS.Middleware.Infrastructure/Repositories/Payments/OutgoingStatusRepository.cs | 005b reviewed |
| src/IPS.Middleware.Infrastructure/Repositories/Payments/PaymentPreparationRepository.cs | 005b reviewed |
| src/IPS.Middleware.Infrastructure/Repositories/Payments/PaymentSubmissionRepository.cs | 005b reviewed |
| src/IPS.Middleware.Infrastructure/Repositories/Payments/TransactionWorkRepository.cs | 005b reviewed |
| src/IPS.Middleware.Infrastructure/Transport/CertificateSettings.cs | 005e reviewed |
| src/IPS.Middleware.Infrastructure/Transport/HttpEndpointSettings.cs | 005e reviewed |
| src/IPS.Middleware.Infrastructure/Transport/HttpEvidence.cs | 005e reviewed |
| src/IPS.Middleware.Infrastructure/Transport/SingleAttemptHttp.cs | 005e reviewed |
| src/IPS.Middleware.Infrastructure/Transport/TransportCertificates.cs | 005e reviewed |
| src/IPS.Middleware.Infrastructure/Transport/TransportPath.cs | 005e reviewed |
| src/IPS.Middleware.Infrastructure/UnitOfWork/UnitOfWork.cs | 005b reviewed |
| src/IPS.Middleware.Api/Configuration/IncomingTransportConfiguration.cs | 005f reviewed |
| src/IPS.Middleware.Api/Configuration/IncomingWorkerConfiguration.cs | 005f reviewed |
| src/IPS.Middleware.Api/Configuration/OutgoingExecutionConfiguration.cs | 005f reviewed |
| src/IPS.Middleware.Api/Configuration/OutgoingTransportConfiguration.cs | 005f reviewed |
| src/IPS.Middleware.Api/Configuration/PaymentSettings.cs | 005f reviewed |
| src/IPS.Middleware.Api/Payments/OutgoingEndpoints.cs | 005f replaced by OutgoingPaymentsController |
| src/IPS.Middleware.Api/Payments/Pacs008RequestMapping.cs | 005f reviewed |
| src/IPS.Middleware.Api/Program.cs | 005f reviewed |
| src/IPS.Middleware.Infrastructure/Persistence/Configurations/IncomingPaymentMetadataConfiguration.cs | 005b reviewed |
| src/IPS.Middleware.Infrastructure/Persistence/Configurations/OutgoingPaymentMetadataConfiguration.cs | 005b reviewed |
| src/IPS.Middleware.Infrastructure/Persistence/Inbound/IncomingPaymentMetadata.cs | 005b reviewed |
| src/IPS.Middleware.Infrastructure/Persistence/Outgoing/OutgoingPaymentMetadata.cs | 005b reviewed |
| src/IPS.Middleware.Infrastructure/Persistence/PersistenceChanges.cs | 005b reviewed |
| src/IPS.Middleware.Infrastructure/Persistence/StagedChanges.cs | 005b reviewed |
| src/IPS.Middleware.Infrastructure/Inbound/Transport/IncomingCbsClient.cs | 005e reviewed |
| src/IPS.Middleware.Infrastructure/Inbound/Transport/IncomingIpsClient.cs | 005e reviewed |
| src/IPS.Middleware.Infrastructure/Payments/Transport/OutgoingIpsClient.cs | 005e reviewed |
| src/IPS.Middleware.Infrastructure/Payments/Transport/OutgoingStatusClient.cs | 005e reviewed |
| src/IPS.Middleware.Infrastructure/Transport/CircuitBreakerSettings.cs | 005e reviewed |
| src/IPS.Middleware.Api/Binding/QueryStringValueBinder.cs | 005f reviewed |
| src/IPS.Middleware.Api/Configuration/OutgoingApiConfiguration.cs | 005f reviewed |
| src/IPS.Middleware.Api/Payments/OutgoingPaymentsController.cs | 005f reviewed |
| src/IPS.Middleware.Api/Binding/HttpJsonInputFormatter.cs | 005f reviewed |
