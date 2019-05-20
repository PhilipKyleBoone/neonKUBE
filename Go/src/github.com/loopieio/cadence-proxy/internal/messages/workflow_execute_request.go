package messages

import (
	"time"

	messagetypes "github.com/loopieio/cadence-proxy/internal/messages/types"

	"go.uber.org/cadence/client"
)

type (

	// WorkflowExecuteRequest is ProxyRequest of MessageType
	// WorkflowExecuteRequest.
	//
	// A WorkflowExecuteRequest contains a reference to a
	// ProxyRequest struct in memory and ReplyType, which is
	// the corresponding MessageType for replying to this ProxyRequest
	//
	// A WorkflowExecuteRequest will pass all of the given data and options
	// necessary to execute a cadence workflow via the cadence client
	WorkflowExecuteRequest struct {
		*ProxyRequest
	}
)

// NewWorkflowExecuteRequest is the default constructor for a WorkflowExecuteRequest
//
// returns *WorkflowExecuteRequest -> a reference to a newly initialized
// WorkflowExecuteRequest in memory
func NewWorkflowExecuteRequest() *WorkflowExecuteRequest {
	request := new(WorkflowExecuteRequest)
	request.ProxyRequest = NewProxyRequest()
	request.Type = messagetypes.WorkflowExecuteRequest
	request.SetReplyType(messagetypes.WorkflowExecuteReply)

	return request
}

// GetDomain gets a WorkflowExecuteRequest's Domain value
// from its properties map
//
// returns *string -> pointer to a string in memory holding the value
// of a WorkflowExecuteRequest's Domain
func (request *WorkflowExecuteRequest) GetDomain() *string {
	return request.GetStringProperty("Domain")
}

// SetDomain sets an WorkflowExecuteRequest's Domain value
// in its properties map
//
// param value *string -> pointer to a string in memory holding the value
// of a WorkflowExecuteRequest's Domain
func (request *WorkflowExecuteRequest) SetDomain(value *string) {
	request.SetStringProperty("Domain", value)
}

// GetName gets a WorkflowExecuteRequest's Name value
// from its properties map
//
// returns *string -> pointer to a string in memory holding the value
// of a WorkflowExecuteRequest's Name
func (request *WorkflowExecuteRequest) GetName() *string {
	return request.GetStringProperty("Name")
}

// SetName sets a WorkflowExecuteRequest's Name value
// in its properties map
//
// param value *string -> a pointer to a string in memory that holds the value
// to be set in the properties map
func (request *WorkflowExecuteRequest) SetName(value *string) {
	request.SetStringProperty("Name", value)
}

// GetArgs gets a WorkflowExecuteRequest's Args field
// from its properties map.  Args is a []byte that hold the arguments
// for executing a specific workflow
//
// returns []byte -> []byte representing workflow parameters or arguments
// for executing
func (request *WorkflowExecuteRequest) GetArgs() []byte {
	return request.GetBytesProperty("Args")
}

// SetArgs sets an WorkflowExecuteRequest's Args field
// from its properties map.  Args is a []byte that hold the arguments
// for executing a specific workflow
//
// param value []byte -> []byte representing workflow parameters or arguments
// for executing
func (request *WorkflowExecuteRequest) SetArgs(value []byte) {
	request.SetBytesProperty("Args", value)
}

// GetOptions gets a WorkflowExecutionRequest's start options
// used to execute a cadence workflow via the cadence workflow client
//
// returns client.StartWorkflowOptions -> a cadence client struct that contains the
// options for executing a workflow
func (request *WorkflowExecuteRequest) GetOptions() *client.StartWorkflowOptions {
	opts := new(client.StartWorkflowOptions)
	err := request.GetJSONProperty("Options", opts)
	if err != nil {
		return nil
	}

	return opts
}

// SetOptions sets a WorkflowExecutionRequest's start options
// used to execute a cadence workflow via the cadence workflow client
//
// param value client.StartWorkflowOptions -> a cadence client struct that contains the
// options for executing a workflow to be set in the WorkflowExecutionRequest's
// properties map
func (request *WorkflowExecuteRequest) SetOptions(value *client.StartWorkflowOptions) {
	request.SetJSONProperty("Options", value)
}

// -------------------------------------------------------------------------
// IProxyMessage interface methods for implementing the IProxyMessage interface

// Clone inherits docs from ProxyRequest.Clone()
func (request *WorkflowExecuteRequest) Clone() IProxyMessage {
	workflowExecuteRequest := NewWorkflowExecuteRequest()
	var messageClone IProxyMessage = workflowExecuteRequest
	request.CopyTo(messageClone)

	return messageClone
}

// CopyTo inherits docs from ProxyRequest.CopyTo()
func (request *WorkflowExecuteRequest) CopyTo(target IProxyMessage) {
	request.ProxyRequest.CopyTo(target)
	if v, ok := target.(*WorkflowExecuteRequest); ok {
		v.SetDomain(request.GetDomain())
		v.SetName(request.GetName())
		v.SetArgs(request.GetArgs())
		v.SetOptions(request.GetOptions())
	}
}

// SetProxyMessage inherits docs from ProxyRequest.SetProxyMessage()
func (request *WorkflowExecuteRequest) SetProxyMessage(value *ProxyMessage) {
	request.ProxyRequest.SetProxyMessage(value)
}

// GetProxyMessage inherits docs from ProxyRequest.GetProxyMessage()
func (request *WorkflowExecuteRequest) GetProxyMessage() *ProxyMessage {
	return request.ProxyRequest.GetProxyMessage()
}

// GetRequestID inherits docs from ProxyRequest.GetRequestID()
func (request *WorkflowExecuteRequest) GetRequestID() int64 {
	return request.ProxyRequest.GetRequestID()
}

// SetRequestID inherits docs from ProxyRequest.SetRequestID()
func (request *WorkflowExecuteRequest) SetRequestID(value int64) {
	request.ProxyRequest.SetRequestID(value)
}

// -------------------------------------------------------------------------
// IProxyRequest interface methods for implementing the IProxyRequest interface

// GetReplyType inherits docs from ProxyRequest.GetReplyType()
func (request *WorkflowExecuteRequest) GetReplyType() messagetypes.MessageType {
	return request.ProxyRequest.GetReplyType()
}

// SetReplyType inherits docs from ProxyRequest.SetReplyType()
func (request *WorkflowExecuteRequest) SetReplyType(value messagetypes.MessageType) {
	request.ProxyRequest.SetReplyType(value)
}

// GetTimeout inherits docs from ProxyRequest.GetTimeout()
func (request *WorkflowExecuteRequest) GetTimeout() time.Duration {
	return request.ProxyRequest.GetTimeout()
}

// SetTimeout inherits docs from ProxyRequest.SetTimeout()
func (request *WorkflowExecuteRequest) SetTimeout(value time.Duration) {
	request.ProxyRequest.SetTimeout(value)
}
