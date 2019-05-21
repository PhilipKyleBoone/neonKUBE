package messages

import (
	"github.com/loopieio/cadence-proxy/internal/cadence/cadenceerrors"
	messagetypes "github.com/loopieio/cadence-proxy/internal/messages/types"
)

type (

	// InitializeReply is a ProxyReply of MessageType
	// InitializeReply.  It holds a reference to a ProxyReply in memory
	InitializeReply struct {
		*ProxyReply
	}
)

// NewInitializeReply is the default constructor for
// a InitializeReply
//
// returns *InitializeReply -> a pointer to a newly initialized
// InitializeReply in memory
func NewInitializeReply() *InitializeReply {
	reply := new(InitializeReply)
	reply.ProxyReply = NewProxyReply()
	reply.Type = messagetypes.InitializeReply

	return reply
}

// -------------------------------------------------------------------------
// IProxyMessage interface methods for implementing the IProxyMessage interface

// Clone inherits docs from ProxyMessage.Clone()
func (reply *InitializeReply) Clone() IProxyMessage {
	initializeReply := NewInitializeReply()
	var messageClone IProxyMessage = initializeReply
	reply.CopyTo(messageClone)

	return messageClone
}

// CopyTo inherits docs from ProxyMessage.CopyTo()
func (reply *InitializeReply) CopyTo(target IProxyMessage) {
	reply.ProxyReply.CopyTo(target)
}

// SetProxyMessage inherits docs from ProxyMessage.SetProxyMessage()
func (reply *InitializeReply) SetProxyMessage(value *ProxyMessage) {
	reply.ProxyReply.SetProxyMessage(value)
}

// GetProxyMessage inherits docs from ProxyMessage.GetProxyMessage()
func (reply *InitializeReply) GetProxyMessage() *ProxyMessage {
	return reply.ProxyReply.GetProxyMessage()
}

// GetRequestID inherits docs from ProxyMessage.GetRequestID()
func (reply *InitializeReply) GetRequestID() int64 {
	return reply.ProxyReply.GetRequestID()
}

// SetRequestID inherits docs from ProxyMessage.SetRequestID()
func (reply *InitializeReply) SetRequestID(value int64) {
	reply.ProxyReply.SetRequestID(value)
}

// -------------------------------------------------------------------------
// IProxyReply interface methods for implementing the IProxyReply interface

// GetError inherits docs from ProxyReply.GetError()
func (reply *InitializeReply) GetError() *cadenceerrors.CadenceError {
	return reply.ProxyReply.GetError()
}

// SetError inherits docs from ProxyReply.SetError()
func (reply *InitializeReply) SetError(value *cadenceerrors.CadenceError) {
	reply.ProxyReply.SetError(value)
}