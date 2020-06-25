//-----------------------------------------------------------------------------
// FILE:		helper.go
// CONTRIBUTOR: John C Burns
// COPYRIGHT:	Copyright (c) 2016-2019 by neonFORGE, LLC.  All rights reserved.
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

package proxyclient

import (
	"context"
	"reflect"
	"strings"
	"sync"
	"time"

	"go.uber.org/cadence"
	"go.uber.org/cadence/.gen/go/cadence/workflowserviceclient"
	cadenceshared "go.uber.org/cadence/.gen/go/shared"
	"go.uber.org/cadence/client"
	"go.uber.org/cadence/encoded"
	"go.uber.org/cadence/worker"
	"go.uber.org/cadence/workflow"
	"go.uber.org/yarpc"
	"go.uber.org/zap"
)

const (

	// _cadenceSystemDomain is the string name of the cadence-system domain that
	// exists on all cadence servers.  This value is used to check that a connection
	// has been established to the cadence server instance and that it is ready to
	// accept requests.
	_cadenceSystemDomain = "cadence-system"

	// _domainNotExistsErrorStr is the string message of the error thrown by
	// cadence when trying to perform and operation on a domain that does not
	// yet exists.
	_domainNotExistErrorStr = "EntityNotExistsError{Message: Domain:"
)

type (

	// ClientHelper holds configuration details for building
	// the cadence domain client and the cadence workflow client
	// This is used for creating, update, and registering cadence domains
	// and stoping/starting cadence workflows workers.
	//
	// Contains:
	// Workflowserviceclient.Interface -> Interface is a client for the WorkflowService service.
	// CadenceClientConfiguration -> configuration information for building the cadence workflow and domain clients.
	// *zap.Logger -> reference to a zap.Logger to log cadence client output to the console.
	// *WorkflowClientBuilder -> reference to a WorkflowClientBuilder used to build the cadence
	// domain and workflow clients.
	// *RegisterDomainRequest -> reference to a RegisterDomainRequest that contains configuration details
	// for registering a cadence domain.
	// client.DomainClient -> cadence domain client instance.
	// client.Client -> cadence workflow client instance.
	ClientHelper struct {
		Service         workflowserviceclient.Interface
		Config          clientConfiguration
		Logger          *zap.Logger
		Builder         *WorkflowClientBuilder
		DomainClient    client.DomainClient
		WorkflowClients *WorkflowClientsMap
		ClientTimeout   time.Duration
	}

	// clientConfiguration contains configuration details for
	// building the cadence workflow and domain clients as well as
	// configuration options for building rpc channels.
	clientConfiguration struct {
		hostPort      string
		domain        string
		clientOptions *client.Options
	}

	// WorkflowClientsMap holds a thread-safe map[interface{}]interface{} of
	// cadence WorkflowClients with their domain.
	WorkflowClientsMap struct {
		sync.Mutex
		clients map[string]client.Client
	}
)

// domainCreated is a flag that prevents a ClientHelper from
// creating a new domain client and registering an existing domain
// var domainCreated bool.

// NewClientHelper is the default constructor
// for a new ClientHelper.
//
// returns *ClientHelper -> pointer to a newly created
// ClientHelper in memory.
func NewClientHelper() *ClientHelper {
	helper := new(ClientHelper)
	helper.WorkflowClients = NewWorkflowClientsMap()
	return helper
}

//----------------------------------------------------------------------------------
// ClientHelper instance methods

// GetHostPort gets the hostPort from a ClientHelper.Config.
//
// returns string -> the hostPort string from a ClientHelper.Config.
func (helper *ClientHelper) GetHostPort() string {
	return helper.Config.hostPort
}

// SetHostPort sets the hostPort in a ClientHelper.Config.
//
// param value string -> the string value to set as the hostPort in
// a ClientHelper.Config.
func (helper *ClientHelper) SetHostPort(value string) {
	helper.Config.hostPort = value
}

// GetDomain gets the domain from a ClientHelper.Config.
//
// returns string -> the domain string from a ClientHelper.Config.
func (helper *ClientHelper) GetDomain() string {
	return helper.Config.domain
}

// SetDomain sets the domain in a ClientHelper.Config.
//
// param value string -> the string value to set as the domain in
// a ClientHelper.Config.
func (helper *ClientHelper) SetDomain(value string) {
	helper.Config.domain = value
}

// GetClientOptions gets the client.Options from a ClientHelper.Config.
//
// returns *client.ClientOptions -> a pointer to a client.Options instance
// in a ClientHelper.Config.
func (helper *ClientHelper) GetClientOptions() *client.Options {
	return helper.Config.clientOptions
}

// SetClientOptions sets the client.Options in a ClientHelper.Config.
//
// param value *client.Options -> client.Options pointer in memory to set
// in ClientHelper.Config.
func (helper *ClientHelper) SetClientOptions(value *client.Options) {
	helper.Config.clientOptions = value
}

// GetClientTimeout gets the ClientTimeout from a ClientHelper instance.
// specifies the amount of time in seconds a reply has to be sent after
// a request has been received by the cadence-proxy.
//
// returns time.Duration -> time.Duration for the ClientTimeout.
func (helper *ClientHelper) GetClientTimeout() time.Duration {
	return helper.ClientTimeout
}

// SetClientTimeout sets the ClientTimeout for a ClientHelper instance.
// specifies the amount of time in seconds a reply has to be sent after
// a request has been received by the cadence-proxy.
//
// param value time.Duration -> time.Duration for the ClientTimeout.
func (helper *ClientHelper) SetClientTimeout(value time.Duration) {
	helper.ClientTimeout = value
}

// SetupServiceConfig configures a ClientHelper's workflowserviceclient.Interface
// Service.  It also sets the Logger, the WorkflowClientBuilder, and acts as a helper for
// creating new cadence workflow and domain clients.
//
// param ctx context.Context -> go context to use to verify a connection
// has been established to the cadence server.
// param retries int32 -> number of time to retry establishing connection with
// cadence server.
// param retryDelay time.Duration -> the amount of time to wait between each
// connection retry.
//
// returns error -> error if there were any problems configuring
// or building the service client.
func (helper *ClientHelper) SetupServiceConfig(
	ctx context.Context,
	retries int32,
	retryDelay time.Duration,
) error {
	if helper.Service != nil {
		return nil
	}

	// Configure the ClientHelper.Builder
	helper.Builder = NewBuilder(helper.Logger).
		SetHostPort(helper.Config.hostPort).
		SetClientOptions(helper.Config.clientOptions).
		SetDomain(helper.Config.domain)

	n := int(retries)
	var err error
	var service workflowserviceclient.Interface

	// build the service client
	for i := 0; i <= n; i++ {
		service, err = helper.Builder.BuildServiceClient()
		if err != nil {
			time.Sleep(retryDelay)
			continue
		}
		break
	}

	if err != nil {
		helper = nil
		return err
	}

	helper.Service = service

	// build the domain client
	domainClient, err := helper.Builder.BuildCadenceDomainClient()
	if err != nil {
		helper.Logger.Error("failed to build domain cadence client.", zap.Error(err))
		return err
	}

	helper.DomainClient = domainClient

	// validate that a connection has been established
	// make a channel that waits for a connection to be established
	// until returning ready
	connectChan := make(chan error)
	defer close(connectChan)

	// poll on system domain
	err = helper.pollDomain(ctx, connectChan, _cadenceSystemDomain)
	if err != nil {
		helper = nil
		return err
	}

	// build the workflow client
	workflowClient, err := helper.Builder.BuildCadenceClient()
	if err != nil {
		helper.Logger.Error("failed to build domain cadence client.", zap.Error(err))
		return nil
	}

	_ = helper.WorkflowClients.Add(helper.Builder.domain, workflowClient)

	return nil
}

// SetupCadenceClients establishes a connection to a running cadence server
// instance and configures domain and workflow clients.
//
// param ctx context.Context -> the context used to poll the server to see if a
// connection has been established.
// param endpoints string -> the endpoints to be set as the cadence service cleint
// HostPort.
// param domain string -> the default domain to configure the workflow client with.
// param retries int32 -> number of time to retry establishing connection with
// cadence server.
// param retryDelay time.Duration -> the amount of time to wait between each
// connection retry.
// param opts *client.Options -> the client options for connection the the cadence
// server instance.
//
// returns error -> error if any errors are thrown while trying to establish a
// connection, or nil upon success.
func (helper *ClientHelper) SetupCadenceClients(
	ctx context.Context,
	endpoints string,
	domain string,
	retries int32,
	retryDelay time.Duration,
	opts *client.Options,
) error {
	helper.SetHostPort(endpoints)
	helper.SetClientOptions(opts)
	helper.SetDomain(domain)
	if err := helper.SetupServiceConfig(ctx, retries, retryDelay); err != nil {
		defer func() {
			helper = nil
		}()

		return err
	}

	return nil
}

// DestroyClient stops the Dispatcher, shutting down all inbounds, outbounds, and transports.
// This function returns after everything has been stopped.
func (helper *ClientHelper) DestroyClient() error {
	return helper.Builder.destroy()
}

// StartWorker starts a workflow worker and activity worker based on configured options.
// The worker will listen for workflows registered with the same taskList.
//
// param domainName string -> name of the cadence doamin for the worker to listen on.
// param taskList string -> the name of the group of cadence workflows for the worker to listen for.
// param options worker.Options -> Options used to configure a worker instance.
// param workerID int64 -> the id of the new worker that will be mapped internally in
// the cadence-proxy.
//
// returns worker.Worker -> the worker.Worker returned by the worker.New()
// call to the cadence server.
// returns error -> an error if the workflow could not be started, or nil if
// the workflow was triggered successfully.
func (helper *ClientHelper) StartWorker(
	domain string,
	taskList string,
	options worker.Options,
) (worker.Worker, error) {
	worker := worker.New(helper.Service, domain, taskList, options)
	err := worker.Start()
	if err != nil {
		helper.Logger.Error("failed to start workers.", zap.Error(err))
		return nil, err
	}

	return worker, nil
}

// StopWorker stops a worker at the given workerID.
//
// param worker.Worker -> the worker to be stopped.
func (helper *ClientHelper) StopWorker(worker worker.Worker) {
	worker.Stop()
}

// DescribeDomain gets the description of a registered cadence domain.
//
// param ctx context.Context -> the context to use to execute the describe domain
// request to cadence.
// param domain string -> the domain you want to query.
//
// returns *cadenceshared.DescribeDomainResponse -> response to the describe domain request.
// returns error -> error if one is thrown, nil if the method executed with no errors.
func (helper *ClientHelper) DescribeDomain(ctx context.Context, domain string) (*cadenceshared.DescribeDomainResponse, error) {
	resp, err := helper.DomainClient.Describe(ctx, domain)
	if err != nil {
		return nil, err
	}

	helper.Logger.Info("Domain Describe Response", zap.Any("Domain Info", *resp.DomainInfo))

	return resp, nil
}

// RegisterDomain registers a cadence domain.
//
// param ctx context.Context -> the context to use to execute the Register domain
// request to cadence.
// param registerDomainRequest *cadenceshared.RegisterDomainRequest -> the request
// used to register the cadence domain.
//
// returns error -> error if one is thrown, nil if the method executed with no errors.
func (helper *ClientHelper) RegisterDomain(ctx context.Context, registerDomainRequest *cadenceshared.RegisterDomainRequest) error {
	domain := registerDomainRequest.GetName()
	err := helper.DomainClient.Register(ctx, registerDomainRequest)
	if err != nil {
		return err
	}

	helper.Logger.Info("domain successfully registered", zap.String("Domain Name", domain))

	return nil
}

// UpdateDomain updates a cadence domain.
//
// param ctx context.Context -> the context to use to execute the Update domain
// request to cadence.
// param UpdateDomainRequest *cadenceshared.UpdateDomainRequest -> the request
// used to Update the cadence domain.
//
// returns error -> error if one is thrown, nil if the method executed with no errors.
func (helper *ClientHelper) UpdateDomain(ctx context.Context, updateDomainRequest *cadenceshared.UpdateDomainRequest) error {
	domain := updateDomainRequest.GetName()
	err := helper.DomainClient.Update(ctx, updateDomainRequest)
	if err != nil {
		helper.Logger.Error("failed to update domain",
			zap.String("Domain Name", domain),
			zap.Error(err))

		return err
	}

	helper.Logger.Info("domain successfully updated", zap.String("Domain Name", domain))

	return nil
}

// ListDomains lists information about the cadence domains.
//
// param ctx context.Context -> the context to use to execute the describe domain
// request to cadence.
// param request *cadenceshared.ListDomainsRequest -> the *cadenceshared.ListDomainsRequest used to
// query cadence for a list of domains.
// param opts ...yarpc.CallOptions -> optional yarpc.CallOption.
//
// returns *cadenceshared.ListDomainsResponse -> response to the describe task list request.
// returns error -> error if one is thrown, nil if the method executed with no errors.
func (helper *ClientHelper) ListDomains(
	ctx context.Context,
	request *cadenceshared.ListDomainsRequest,
	opts ...yarpc.CallOption,
) (*cadenceshared.ListDomainsResponse, error) {
	resp, err := helper.Service.ListDomains(ctx, request)
	if err != nil {
		return nil, err
	}

	helper.Logger.Info("List domains response", zap.Any("Domains", resp.Domains))

	return resp, nil
}

// ExecuteWorkflow is an instance method to execute a registered cadence workflow.
//
// param ctx context.Context -> the context to use to execute the workflow.
// param domain string -> the domain to start the workflow on.
// param options client.StartWorkflowOptions -> configuration parameters for starting a workflow execution.
// param workflow interface{} -> a registered cadence workflow.
// param args ...interface{} -> anonymous number of arguments for starting a workflow.
//
// returns client.WorkflowRun -> the client.WorkflowRun returned by the workflow execution
// call to the cadence server.
// returns error -> an error if the workflow could not be started, or nil if
// the workflow was triggered successfully.
func (helper *ClientHelper) ExecuteWorkflow(
	ctx context.Context,
	domain string,
	options client.StartWorkflowOptions,
	workflow interface{},
	args ...interface{},
) (client.WorkflowRun, error) {
	n := 30
	var err error
	var workflowRun client.WorkflowRun
	workflowClient := helper.GetOrCreateWorkflowClient(domain)

	// start the workflow, but put in a loop
	// to check if the domain has been detected yet
	// by cadence server (primarily for unit testing,
	// loop should never execute more than once in production)
	for i := 0; i < n; i++ {
		workflowRun, err = workflowClient.ExecuteWorkflow(ctx, options, workflow, args...)
		if err != nil {
			if (strings.Contains(err.Error(), _domainNotExistErrorStr)) && (i < n-1) {
				time.Sleep(time.Second)
				continue
			}

			helper.Logger.Error("failed to create workflow", zap.Error(err))
			return nil, err
		}
		break
	}

	helper.Logger.Info("Started Workflow",
		zap.String("WorkflowID", workflowRun.GetID()),
		zap.String("RunID", workflowRun.GetRunID()))

	return workflowRun, nil
}

// GetWorkflow is an instance method to get a WorkflowRun from a started
// cadence workflow.
//
// param ctx context.Context -> the context to use to get the workflow.
// param workflowID string -> the workflowID of the running workflow.
// param runID string -> the runID of the running workflow.
// param domain string -> the domain the workflow is executing on.
//
// returns client.WorkflowRun -> the client.WorkflowRun returned by the GetWorkflow
// call to the cadence server.
// returns error -> an error if the workflow could not be started, or nil if
// the workflow was triggered successfully.
func (helper *ClientHelper) GetWorkflow(
	ctx context.Context,
	workflowID string,
	runID string,
	domain string,
) (client.WorkflowRun, error) {
	workflowClient := helper.GetOrCreateWorkflowClient(domain)
	workflowRun := workflowClient.GetWorkflow(ctx, workflowID, runID)

	helper.Logger.Info("Get Workflow",
		zap.String("WorkflowID", workflowRun.GetID()),
		zap.String("RunID", workflowRun.GetRunID()))

	return workflowRun, nil
}

// DescribeTaskList gets the description of a registered cadence domain.
//
// param ctx context.Context -> the context to use to execute the describe domain
// request to cadence.
// param request *cadenceshared.DescribeTaskListRequest -> the *cadenceshared.DescribeTaskListRequest used to
// query cadence for a task list.
// param opts ...yarpc.CallOptions -> optional yarpc.CallOption.
//
// returns *cadenceshared.DescribeTaskListResponse -> response to the describe task list request.
// request
// returns error -> error if one is thrown, nil if the method executed with no errors.
func (helper *ClientHelper) DescribeTaskList(
	ctx context.Context,
	request *cadenceshared.DescribeTaskListRequest,
	opts ...yarpc.CallOption,
) (*cadenceshared.DescribeTaskListResponse, error) {
	resp, err := helper.Service.DescribeTaskList(ctx, request)
	if err != nil {
		return nil, err
	}

	helper.Logger.Info("TaskList Describe Response", zap.String("TaskList Info", resp.String()))

	return resp, nil
}

// CancelWorkflow is an instance method to cancel running
// cadence workflow.
//
// param ctx context.Context -> the context to use to cancel the workflow.
// param workflowID string -> the workflowID of the running workflow.
// param runID string -> the runID of the running workflow.
// param domain string -> the domain the workflow is executing on.
//
// returns error -> an error if the workflow could not be started, or nil if
// the workflow was cancelled successfully.
func (helper *ClientHelper) CancelWorkflow(
	ctx context.Context,
	workflowID string,
	runID string,
	domain string,
) error {
	workflowClient := helper.GetOrCreateWorkflowClient(domain)
	err := workflowClient.CancelWorkflow(ctx, workflowID, runID)
	if err != nil {
		helper.Logger.Error("failed to cancel workflow", zap.Error(err))
		return err
	}

	helper.Logger.Info("Workflow Cancelled",
		zap.String("WorkflowID", workflowID),
		zap.String("RunID", runID))

	return nil
}

// TerminateWorkflow is an instance method to terminate a running
// cadence workflow.
//
// param ctx context.Context -> the context to use to terminate the workflow.
// param workflowID string -> the workflowID of the running workflow.
// param runID string -> the runID of the running workflow.
// param domain string -> the domain the workflow is executing on.
// param reason string -> the string reason for terminating.
// param details []byte -> termination details encoded as a []byte.
//
// returns error -> an error if the workflow could not be started, or nil if
// the workflow was terminated successfully.
func (helper *ClientHelper) TerminateWorkflow(
	ctx context.Context,
	workflowID string,
	runID string,
	domain string,
	reason string,
	details []byte,
) error {
	workflowClient := helper.GetOrCreateWorkflowClient(domain)
	err := workflowClient.TerminateWorkflow(
		ctx,
		workflowID,
		runID,
		reason,
		details)

	if err != nil {
		helper.Logger.Error("failed to terminate workflow", zap.Error(err))
		return err
	}

	helper.Logger.Info("Workflow Terminated",
		zap.String("WorkflowID", workflowID),
		zap.String("RunID", runID))

	return nil
}

// SignalWithStartWorkflow is an instance method to signal a cadence workflow to start.
//
// param ctx context.Context -> the context to use to get the workflow.
// param workflowID string -> the workflowID of the running workflow.
// param domain string -> the domain the workflow is executing on.
// param signalName string -> name of the signal to signal channel to signal the workflow.
// param signalArg []byte -> the signalling arguments encoded as a []byte.
// param signalOpts client.StartWorkflowOptions -> client.StartWorkflowOptions
// used to start the workflow.
// param workflow string -> the name of the workflow to start.
// param args ...interface{} -> the optional arguments for starting the workflow.
//
// returns *workflow.Execution -> pointer to the resulting workflow execution from
// starting the workflow.
// returns error -> error upon failure and nil upon success.
func (helper *ClientHelper) SignalWithStartWorkflow(
	ctx context.Context,
	workflowID string,
	domain string,
	signalName string,
	signalArg []byte,
	opts client.StartWorkflowOptions,
	workflow string,
	args ...interface{},
) (*workflow.Execution, error) {
	workflowClient := helper.GetOrCreateWorkflowClient(domain)
	workflowExecution, err := workflowClient.SignalWithStartWorkflow(
		ctx,
		workflowID,
		signalName,
		signalArg,
		opts,
		workflow,
		args...)

	if err != nil {
		helper.Logger.Error("failed to start workflow", zap.Error(err))
		return nil, err
	}

	helper.Logger.Info("Started Workflow",
		zap.String("Workflow", workflow),
		zap.String("WorkflowID", workflowExecution.ID),
		zap.String("RunID", workflowExecution.RunID))

	return workflowExecution, nil
}

// DescribeWorkflowExecution is an instance method to describe the execution
// of a running cadence workflow.
//
// param ctx context.Context -> the context to use to cancel the workflow.
// param workflowID string -> the workflowID of the running workflow.
// param runID string -> the runID of the running workflow.
// param domain string -> the domain the workflow is executing on.
//
// returns *cadenceshared.DescribeWorkflowExecutionResponse -> the response to the
// describe workflow execution request.
// returns error -> an error if the workflow could not be started, or nil if
// the workflow was cancelled successfully.
func (helper *ClientHelper) DescribeWorkflowExecution(
	ctx context.Context,
	workflowID string,
	runID string,
	domain string,
) (*cadenceshared.DescribeWorkflowExecutionResponse, error) {
	workflowClient := helper.GetOrCreateWorkflowClient(domain)
	response, err := workflowClient.DescribeWorkflowExecution(ctx, workflowID, runID)

	if err != nil {
		helper.Logger.Error("failed to describe workflow execution", zap.Error(err))
		return nil, err
	}

	helper.Logger.Info("Workflow Describe Execution Successful", zap.Any("Execution Info", *response.WorkflowExecutionInfo))

	return response, nil
}

// SignalWorkflow is an instance method to signal a cadence workflow.
//
// param ctx context.Context -> the context to use to get the workflow.
// param workflowID string -> the workflowID of the running workflow.
// param runID string -> the runID of the running cadence workflow.
// param domain string -> the domain the workflow is executing on.
// param signalName string -> name of the signal to signal channel to signal the workflow.
// param arg interface{} -> the signaling arguments.
//
// returns error -> error upon failure and nil upon success.
func (helper *ClientHelper) SignalWorkflow(
	ctx context.Context,
	workflowID string,
	runID string,
	domain string,
	signalName string,
	arg interface{},
) error {
	workflowClient := helper.GetOrCreateWorkflowClient(domain)
	err := workflowClient.SignalWorkflow(
		ctx,
		workflowID,
		runID,
		signalName,
		arg)

	if err != nil {
		helper.Logger.Error("failed to signal workflow", zap.Error(err))
		return err
	}

	helper.Logger.Info("Successfully Signaled Workflow",
		zap.String("SignalName", signalName),
		zap.String("WorkflowID", workflowID),
		zap.String("RunID", runID))

	return nil
}

// QueryWorkflow is an instance method to query a cadence workflow.
//
// param ctx context.Context -> the context to use to get the workflow.
// param workflowID string -> the workflowID of the running workflow.
// param runID string -> the runID of the running cadence workflow.
// param domain string -> the domain the workflow is executing on.
// param queryType string -> name of the query to query channel to query the workflow.
// param args ...interface{} -> the optional querying arguments.
//
// returns encoded.Value -> the encoded result value of querying a workflow.
// returns error -> error upon failure and nil upon success.
func (helper *ClientHelper) QueryWorkflow(
	ctx context.Context,
	workflowID string,
	runID string,
	domain string,
	queryType string,
	args ...interface{},
) (encoded.Value, error) {
	workflowClient := helper.GetOrCreateWorkflowClient(domain)
	value, err := workflowClient.QueryWorkflow(
		ctx,
		workflowID,
		runID,
		queryType,
		args...)

	if err != nil {
		helper.Logger.Error("failed to query workflow", zap.Error(err))
		return nil, err
	}

	helper.Logger.Info("Successfully Queried Workflow",
		zap.String("QueryType", queryType),
		zap.String("WorkflowID", workflowID),
		zap.String("RunID", runID))

	return value, nil
}

// CompleteActivity is an instance method that completes the execution of an activity.
//
// param ctx context.Context -> the go context used to execute the complete activity call.
// param taskToken []byte -> a task token used to complete the activity encoded as
// a []byte.
// param domain string -> the domain the workflow is executing on.
// param result interface{} -> the result to complete the activity with.
// pararm cadenceError *proxyerror.CadenceError -> error to complete the activity with.
//
// returns error -> error upon failure to complete the activity, nil upon success.
func (helper *ClientHelper) CompleteActivity(
	ctx context.Context,
	taskToken []byte,
	domain string,
	result interface{},
	completionErr error,
) error {
	var cadenceError error
	if completionErr != nil && !reflect.ValueOf(completionErr).IsNil() {
		cadenceError = cadence.NewCustomError(completionErr.Error())
	}

	// query the workflow
	workflowClient := helper.GetOrCreateWorkflowClient(domain)
	err := workflowClient.CompleteActivity(ctx, taskToken, result, cadenceError)
	if err != nil {
		helper.Logger.Error("failed to complete activity", zap.Error(err))
		return err
	}

	helper.Logger.Info("Successfully Completed Activity",
		zap.Any("Result", result),
		zap.Error(cadenceError))

	return nil
}

// CompleteActivityByID is an instance method that completes the execution of an activity.
//
// param ctx context.Context -> the go context used to execute the complete activity call.
// param domain string -> the domain the activity to complete is running on.
// param workflowID string -> the workflowID of the running workflow.
// param runID string -> the runID of the running cadence workflow.
// param activityID string -> the activityID of the executing activity to complete.
// param result interface{} -> the result to complete the activity with.
// pararm cadenceError *proxyerror.CadenceError -> error to complete the activity with.
//
// returns error -> error upon failure to complete the activity, nil upon success.
func (helper *ClientHelper) CompleteActivityByID(
	ctx context.Context,
	domain string,
	workflowID string,
	runID string,
	activityID string,
	result interface{},
	completionErr error,
) error {
	var cadenceError error
	if completionErr != nil && !reflect.ValueOf(completionErr).IsNil() {
		cadenceError = cadence.NewCustomError(completionErr.Error())
	}

	// query the workflow
	workflowClient := helper.GetOrCreateWorkflowClient(domain)
	err := workflowClient.CompleteActivityByID(
		ctx,
		domain,
		workflowID,
		runID,
		activityID,
		result,
		cadenceError)

	if err != nil {
		helper.Logger.Error("failed to complete activity", zap.Error(err))
		return err
	}

	helper.Logger.Info("Successfully Completed Activity",
		zap.String("ActivityId", activityID),
		zap.Any("Result", result),
		zap.Error(cadenceError))

	return nil
}

// RecordActivityHeartbeat is an instance method that records heartbeat for an activity.
//
// param ctx context.Context -> the go context used to record a heartbeat for an activity.
// param taskToken []byte -> a task token used to record a heartbeat for an activity
// a []byte.
// param domain string -> the domain the workflow is executing on.
// param details ...interface{} -> optional activity heartbeat details.
//
// returns error -> error upon failure to record activity heartbeat, nil upon success.
func (helper *ClientHelper) RecordActivityHeartbeat(
	ctx context.Context,
	taskToken []byte,
	domain string,
	details ...interface{},
) error {
	workflowClient := helper.GetOrCreateWorkflowClient(domain)
	err := workflowClient.RecordActivityHeartbeat(ctx, taskToken, details)

	if err != nil {
		helper.Logger.Error("failed to record activity heartbeat", zap.Error(err))
		return err
	}

	helper.Logger.Info("Successfully Recorded Activity Heartbeat", zap.Any("Details", details))

	return nil
}

// RecordActivityHeartbeatByID is an instance method that records heartbeat for an activity.
//
// param ctx context.Context -> the go context used to record a heartbeat for an activity.
// param domain string -> the domain the activity to is running in.
// param workflowID string -> the workflowID of the running workflow.
// param runID string -> the runID of the running cadence workflow.
// param activityID string -> the activityID of the executing activity.
// param details ...interface{} -> optional activity heartbeat details.
//
// returns error -> error upon failure to record activity heartbeat, nil upon success.
func (helper *ClientHelper) RecordActivityHeartbeatByID(
	ctx context.Context,
	domain string,
	workflowID string,
	runID string,
	activityID string,
	details ...interface{},
) error {
	workflowClient := helper.GetOrCreateWorkflowClient(domain)
	err := workflowClient.RecordActivityHeartbeatByID(
		ctx,
		domain,
		workflowID,
		runID,
		activityID,
		details)

	if err != nil {
		helper.Logger.Error("failed to record activity heartbeat", zap.Error(err))
		return err
	}

	helper.Logger.Info("Successfully Recorded Activity Heartbeat",
		zap.String("ActivityId", activityID),
		zap.String("WorkflowID", workflowID),
		zap.String("RunID", runID),
		zap.Any("Details", details))

	return nil
}

// GetOrCreateWorkflowClient queries workflowClients looking for
// a cadence WorkflowClient at a specified domain.
//
// param domain string -> the domain of the cadence WorkflowClient.
//
// returns client.Client -> the WorkflowClient associated with
// the specified domain.
func (helper *ClientHelper) GetOrCreateWorkflowClient(domain string) client.Client {
	wc := helper.WorkflowClients.Get(domain)
	if wc == nil {
		wc = client.NewClient(
			helper.Service,
			domain,
			helper.Builder.clientOptions)

		_ = helper.WorkflowClients.Add(domain, wc)
	}

	return wc
}

// pollDomain polls the cadence server to check and see if a connection
// has been established by the service client by polling a domain.
//
// param ctx context.Context -> context to execute the domain describe call on.
// param channel chan error -> channel to send error over upon a connection
// failure or nil if a connection was verified.
// param domain string -> the domain to query for a connection.
//
// returns error -> error if establishing a connection failed and nil
// upon success.
func (helper *ClientHelper) pollDomain(
	ctx context.Context,
	channel chan error,
	domain string,
) error {
	go func() {
		var err error
		defer func() {
			channel <- err
		}()

		_, err = helper.DescribeDomain(ctx, domain)
	}()

	// block and catch the result
	if err := <-channel; err != nil {
		return err
	}

	return nil
}

//----------------------------------------------------------------------------
// WorkflowClientsMap instance methods

// NewWorkflowClientsMap is the constructor for an WorkflowClientsMap
func NewWorkflowClientsMap() *WorkflowClientsMap {
	o := new(WorkflowClientsMap)
	o.clients = make(map[string]client.Client)
	return o
}

// Add adds a new cadence WorkflowClient and its corresponding domain into
// the WorkflowClientsMap map.  This method is thread-safe.
//
// param domain string -> the domain for the cadence WorkflowClient.
// This will be the mapped key.
// param wc client.Client -> cadence WorkflowClient used to
// execute workflow functions. This will be the mapped value.
//
// returns string -> the domain for the cadence WorkflowClient added to the map.
func (wcm *WorkflowClientsMap) Add(domain string, wc client.Client) string {
	wcm.Lock()
	defer wcm.Unlock()
	wcm.clients[domain] = wc
	return domain
}

// Remove removes key/value entry from the WorkflowClientsMap map at the specified
// ContextId.  This is a thread-safe method.
//
// param domain string -> the domain for the cadence WorkflowClient.
// This will be the mapped key.
//
// returns string -> the domain for the cadence WorkflowClient removed from the map.
func (wcm *WorkflowClientsMap) Remove(domain string) string {
	wcm.Lock()
	defer wcm.Unlock()
	delete(wcm.clients, domain)
	return domain
}

// Get gets a WorkflowContext from the WorkflowClientsMap at the specified
// ContextID.  This method is thread-safe.
//
// param domain string -> the domain for the cadence WorkflowClient.
// This will be the mapped key.
//
// returns client.Client -> pointer to cadence WorkflowClient with the specified domain.
func (wcm *WorkflowClientsMap) Get(domain string) client.Client {
	wcm.Lock()
	defer wcm.Unlock()
	return wcm.clients[domain]
}
