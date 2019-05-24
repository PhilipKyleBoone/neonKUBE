package messages

import (
	"github.com/loopieio/cadence-proxy/internal/cadence/cadenceerrors"
	messagetypes "github.com/loopieio/cadence-proxy/internal/messages/types"
)

type (

	// WorkflowListOpenExecutionsReply is a WorkflowReply of MessageType
	// WorkflowListOpenExecutionsReply.  It holds a reference to a WorkflowReply in memory
	// and is the reply type to a WorkflowListOpenExecutionsRequest
	WorkflowListOpenExecutionsReply struct {
		*WorkflowReply
	}
)

// NewWorkflowListOpenExecutionsReply is the default constructor for
// a WorkflowListOpenExecutionsReply
//
// returns *WorkflowListOpenExecutionsReply -> a pointer to a newly initialized
// WorkflowListOpenExecutionsReply in memory
func NewWorkflowListOpenExecutionsReply() *WorkflowListOpenExecutionsReply {
	reply := new(WorkflowListOpenExecutionsReply)
	reply.WorkflowReply = NewWorkflowReply()
	reply.Type = messagetypes.WorkflowListOpenExecutionsReply

	return reply
}

// -------------------------------------------------------------------------
// IProxyMessage interface methods for implementing the IProxyMessage interface

// Clone inherits docs from WorkflowReply.Clone()
func (reply *WorkflowListOpenExecutionsReply) Clone() IProxyMessage {
	workflowListOpenExecutionsReply := NewWorkflowListOpenExecutionsReply()
	var messageClone IProxyMessage = workflowListOpenExecutionsReply
	reply.CopyTo(messageClone)

	return messageClone
}

// CopyTo inherits docs from WorkflowReply.CopyTo()
func (reply *WorkflowListOpenExecutionsReply) CopyTo(target IProxyMessage) {
	reply.WorkflowReply.CopyTo(target)
}

// SetProxyMessage inherits docs from WorkflowReply.SetProxyMessage()
func (reply *WorkflowListOpenExecutionsReply) SetProxyMessage(value *ProxyMessage) {
	reply.WorkflowReply.SetProxyMessage(value)
}

// GetProxyMessage inherits docs from WorkflowReply.GetProxyMessage()
func (reply *WorkflowListOpenExecutionsReply) GetProxyMessage() *ProxyMessage {
	return reply.WorkflowReply.GetProxyMessage()
}

// GetRequestID inherits docs from WorkflowReply.GetRequestID()
func (reply *WorkflowListOpenExecutionsReply) GetRequestID() int64 {
	return reply.WorkflowReply.GetRequestID()
}

// SetRequestID inherits docs from WorkflowReply.SetRequestID()
func (reply *WorkflowListOpenExecutionsReply) SetRequestID(value int64) {
	reply.WorkflowReply.SetRequestID(value)
}

// -------------------------------------------------------------------------
// IProxyReply interface methods for implementing the IProxyReply interface

// GetError inherits docs from WorkflowReply.GetError()
func (reply *WorkflowListOpenExecutionsReply) GetError() *cadenceerrors.CadenceError {
	return reply.WorkflowReply.GetError()
}

// SetError inherits docs from WorkflowReply.SetError()
func (reply *WorkflowListOpenExecutionsReply) SetError(value *cadenceerrors.CadenceError) {
	reply.WorkflowReply.SetError(value)
}

// -------------------------------------------------------------------------
// IWorkflowReply interface methods for implementing the IWorkflowReply interface

// GetWorkflowContextID inherits docs from WorkflowReply.GetWorkflowContextID()
func (reply *WorkflowListOpenExecutionsReply) GetWorkflowContextID() int64 {
	return reply.WorkflowReply.GetWorkflowContextID()
}

// SetWorkflowContextID inherits docs from WorkflowReply.GetWorkflowContextID()
func (reply *WorkflowListOpenExecutionsReply) SetWorkflowContextID(value int64) {
	reply.WorkflowReply.SetWorkflowContextID(value)
}