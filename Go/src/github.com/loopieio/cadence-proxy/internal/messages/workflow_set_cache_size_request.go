package messages

import (
	"time"

	messagetypes "github.com/loopieio/cadence-proxy/internal/messages/types"
)

type (

	// WorkflowSetCacheSizeRequest is ProxyRequest of MessageType
	// WorkflowSetCacheSizeRequest.
	//
	// A WorkflowSetCacheSizeRequest contains a reference to a
	// ProxyRequest struct in memory and ReplyType, which is
	// the corresponding MessageType for replying to this ProxyRequest
	//
	// A WorkflowSetCacheSizeRequest sets the maximum number of bytes the client will use
	/// to cache the history of a sticky workflow on a workflow worker as a performance
	/// optimization.  When this is exceeded for a workflow, its full history will
	/// need to be retrieved from the Cadence cluster the next time the workflow
	/// instance is assigned to a worker.
	WorkflowSetCacheSizeRequest struct {
		*ProxyRequest
	}
)

// NewWorkflowSetCacheSizeRequest is the default constructor for a WorkflowSetCacheSizeRequest
//
// returns *WorkflowSetCacheSizeRequest -> a reference to a newly initialized
// WorkflowSetCacheSizeRequest in memory
func NewWorkflowSetCacheSizeRequest() *WorkflowSetCacheSizeRequest {
	request := new(WorkflowSetCacheSizeRequest)
	request.ProxyRequest = NewProxyRequest()
	request.Type = messagetypes.WorkflowSetCacheSizeRequest
	request.SetReplyType(messagetypes.WorkflowSetCacheSizeReply)

	return request
}

// GetSize gets a WorkflowSetCacheSizeRequest's Size value
// from its properties map.  Specifies the maximum number of bytes used for
// caching sticky workflows.
//
// returns int -> int specifying the maximum number of bytes used for caching
// sticky workflows.cache Size
func (request *WorkflowSetCacheSizeRequest) GetSize() int {
	return int(request.GetLongProperty("Size"))
}

// SetSize sets a WorkflowSetCacheSizeRequest's Size value
// in its properties map
//
// param value int -> int specifying the maximum number of bytes used for caching
// sticky workflows.cache Size
func (request *WorkflowSetCacheSizeRequest) SetSize(value int) {
	request.SetLongProperty("Size", int64(value))
}

// -------------------------------------------------------------------------
// IProxyMessage interface methods for implementing the IProxyMessage interface

// Clone inherits docs from ProxyRequest.Clone()
func (request *WorkflowSetCacheSizeRequest) Clone() IProxyMessage {
	workflowSetCacheSizeRequest := NewWorkflowSetCacheSizeRequest()
	var messageClone IProxyMessage = workflowSetCacheSizeRequest
	request.CopyTo(messageClone)

	return messageClone
}

// CopyTo inherits docs from ProxyRequest.CopyTo()
func (request *WorkflowSetCacheSizeRequest) CopyTo(target IProxyMessage) {
	request.ProxyRequest.CopyTo(target)
	if v, ok := target.(*WorkflowSetCacheSizeRequest); ok {
		v.SetSize(request.GetSize())
	}
}

// SetProxyMessage inherits docs from ProxyRequest.SetProxyMessage()
func (request *WorkflowSetCacheSizeRequest) SetProxyMessage(value *ProxyMessage) {
	request.ProxyRequest.SetProxyMessage(value)
}

// GetProxyMessage inherits docs from ProxyRequest.GetProxyMessage()
func (request *WorkflowSetCacheSizeRequest) GetProxyMessage() *ProxyMessage {
	return request.ProxyRequest.GetProxyMessage()
}

// GetRequestID inherits docs from ProxyRequest.GetRequestID()
func (request *WorkflowSetCacheSizeRequest) GetRequestID() int64 {
	return request.ProxyRequest.GetRequestID()
}

// SetRequestID inherits docs from ProxyRequest.SetRequestID()
func (request *WorkflowSetCacheSizeRequest) SetRequestID(value int64) {
	request.ProxyRequest.SetRequestID(value)
}

// -------------------------------------------------------------------------
// IProxyRequest interface methods for implementing the IProxyRequest interface

// GetReplyType inherits docs from ProxyRequest.GetReplyType()
func (request *WorkflowSetCacheSizeRequest) GetReplyType() messagetypes.MessageType {
	return request.ProxyRequest.GetReplyType()
}

// SetReplyType inherits docs from ProxyRequest.SetReplyType()
func (request *WorkflowSetCacheSizeRequest) SetReplyType(value messagetypes.MessageType) {
	request.ProxyRequest.SetReplyType(value)
}

// GetTimeout inherits docs from ProxyRequest.GetTimeout()
func (request *WorkflowSetCacheSizeRequest) GetTimeout() time.Duration {
	return request.ProxyRequest.GetTimeout()
}

// SetTimeout inherits docs from ProxyRequest.SetTimeout()
func (request *WorkflowSetCacheSizeRequest) SetTimeout(value time.Duration) {
	request.ProxyRequest.SetTimeout(value)
}
