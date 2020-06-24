//-----------------------------------------------------------------------------
// FILE:		utils.go
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

package endpoints

import (
	"bytes"
	"fmt"
	"io"
	"io/ioutil"
	"net/http"
	"strings"

	"go.uber.org/cadence/workflow"

	"github.com/cadence-proxy/internal"
	proxyactivity "github.com/cadence-proxy/internal/cadence/activity"
	proxyclient "github.com/cadence-proxy/internal/cadence/client"
	proxyworker "github.com/cadence-proxy/internal/cadence/worker"
	proxyworkflow "github.com/cadence-proxy/internal/cadence/workflow"
	"github.com/cadence-proxy/internal/messages"
)

//----------------------------------------------------------------------------
// ProxyMessage processing helpers

// CheckRequestValidity checks to make sure that the request is in
// the correct format to be handled
func CheckRequestValidity(w http.ResponseWriter, r *http.Request) (int, error) {
	if r.Header.Get("Content-Type") != internal.ContentType {
		err := fmt.Errorf("incorrect Content-Type %s. Content must be %s",
			r.Header.Get("Content-Type"),
			internal.ContentType)

		return http.StatusBadRequest, err
	}

	if r.Method != http.MethodPut {
		err := fmt.Errorf("invalid HTTP Method: %s, must be HTTP Metho: %s",
			r.Method,
			http.MethodPut)

		return http.StatusMethodNotAllowed, err
	}

	return http.StatusOK, nil
}

// ReadAndDeserialize reads the ProxyMessage from the request body and
// deserializes it into the corresponding message type.
func ReadAndDeserialize(body io.Reader) (messages.IProxyMessage, error) {
	payload, err := ioutil.ReadAll(body)
	if err != nil {
		return nil, err
	}

	// deserialize the payload
	buf := bytes.NewBuffer(payload)
	message, err := messages.Deserialize(buf, false)
	if err != nil {
		return nil, err
	}

	return message, nil
}

// putToNeonCadenceClient sends an IProxyMessage to the .NET client.
func putToNeonCadenceClient(message messages.IProxyMessage) (*http.Response, error) {
	proxyMessage := message.GetProxyMessage()

	// serialize the message
	content, err := proxyMessage.Serialize(false)
	if err != nil {
		return nil, err
	}

	// create a buffer with the serialized bytes to reply with
	// and create the PUT request
	buf := bytes.NewBuffer(content)
	req, err := http.NewRequest(http.MethodPut, replyAddress, buf)
	if err != nil {
		return nil, err
	}

	// set the request header to specified content type
	// and disable http request compression
	req.Header.Set("Content-Type", internal.ContentType)
	req.Header.Set("Accept-Encoding", "identity")

	// initialize the http.Client and send the request
	resp, err := HttpClient.Do(req)
	if err != nil {
		return nil, err
	}

	return resp, nil
}

// setReplayStatus checks a workflow context to see if it is replaying.
// Sets the replay status of specified invoke and reply messages to the .NET client.
func setReplayStatus(ctx workflow.Context, message messages.IProxyMessage) {
	isReplaying := workflow.IsReplaying(ctx)
	switch s := message.(type) {
	case messages.IWorkflowReply:
		if isReplaying {
			s.SetReplayStatus(proxyworkflow.ReplayStatusReplaying)
		} else {
			s.SetReplayStatus(proxyworkflow.ReplayStatusNotReplaying)
		}

	case *messages.WorkflowInvokeRequest:
		if isReplaying {
			s.SetReplayStatus(proxyworkflow.ReplayStatusReplaying)
		} else {
			s.SetReplayStatus(proxyworkflow.ReplayStatusNotReplaying)
		}

	case *messages.WorkflowQueryInvokeRequest:
		if isReplaying {
			s.SetReplayStatus(proxyworkflow.ReplayStatusReplaying)
		} else {
			s.SetReplayStatus(proxyworkflow.ReplayStatusNotReplaying)
		}

	case *messages.WorkflowSignalInvokeRequest:
		if isReplaying {
			s.SetReplayStatus(proxyworkflow.ReplayStatusReplaying)
		} else {
			s.SetReplayStatus(proxyworkflow.ReplayStatusNotReplaying)
		}
	}
}

func sendMessage(message messages.IProxyMessage) {
	resp, err := putToNeonCadenceClient(message)
	if err != nil {
		panic(err)
	}
	defer func() {
		err := resp.Body.Close()
		if err != nil {
			panic(err)
		}
	}()
}

// sendFutureACK sends an acknowledgement of a workflow future being created
// to the .NET client.
// func sendFutureACK(contextID, operationID, clientID int64) *Operation {

// 	// create the WorkflowFutureReadyRequest
// 	requestID := NextRequestID()
// 	workflowFutureReadyRequest := messages.NewWorkflowFutureReadyRequest()
// 	workflowFutureReadyRequest.SetRequestID(requestID)
// 	workflowFutureReadyRequest.SetContextID(contextID)
// 	workflowFutureReadyRequest.SetFutureOperationID(operationID)
// 	workflowFutureReadyRequest.SetClientID(clientID)

// 	// create the Operation for this request and add it to the operations map
// 	op := NewOperation(requestID, workflowFutureReadyRequest)
// 	op.SetChannel(make(chan interface{}))
// 	op.SetContextID(contextID)
// 	Operations.Add(requestID, op)

// 	// send the request
// 	go sendMessage(workflowFutureReadyRequest)

// 	return op
// }

// isCanceledError checks a golang error or a
// CadenceError to see if it is a canceledError.
func isCanceledErr(err error) bool {
	return strings.Contains(err.Error(), "CanceledError")
}

// isForceReplayError checks if an error is
// a force replay error.
func isForceReplayErr(err error) bool {
	return strings.Contains(err.Error(), "force-replay")
}

// verifyClientHelper verifies the existence of a ClientHelper for specified
// IProxyRequests before sending them down to the request handlers.
func verifyClientHelper(request messages.IProxyRequest, helper *proxyclient.ClientHelper) error {
	switch request.GetType() {
	case internal.InitializeRequest,
		internal.PingRequest,
		internal.ConnectRequest,
		internal.TerminateRequest,
		internal.CancelRequest,
		internal.HeartbeatRequest:
		return nil

	default:
		if helper == nil {
			return internal.ErrConnection
		}
	}

	return nil
}

// NextContextID calls proxyworkflow.NextContextID().
// Iterates workflow contextID.
func NextContextID() int64 {
	return proxyworkflow.NextContextID()
}

// NextActivityContextID calls proxyactivity.NextContextID().
// Iterates activity contextID.
func NextActivityContextID() int64 {
	return proxyactivity.NextContextID()
}

// NextWorkerID calls proxyworker.NextWorkerID().
// Iterates workerID.
func NextWorkerID() int64 {
	return proxyworker.NextWorkerID()
}
