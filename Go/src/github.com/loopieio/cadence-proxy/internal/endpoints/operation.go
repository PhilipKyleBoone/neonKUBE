package endpoints

import (
	"errors"
	"sync"

	"go.uber.org/cadence"
	"go.uber.org/cadence/workflow"

	"github.com/loopieio/cadence-proxy/internal/messages"
)

var (

	// Operations is a map of operations used to track pending
	// cadence-client operations
	Operations = new(operationsMap)
)

type (
	operationsMap struct {
		sync.Map
	}

	// Operation is used to track pending Neon.Cadence library calls
	Operation struct {
		future      workflow.Future
		settable    workflow.Settable
		requestID   int64
		request     messages.IProxyRequest
		isCancelled bool
		channel     chan interface{}
	}
)

// NewOperation is the default constructor for an Operation
func NewOperation(requestID int64, request messages.IProxyRequest) *Operation {
	op := new(Operation)
	op.isCancelled = false
	op.request = request
	op.requestID = requestID

	return op
}

//----------------------------------------------------------------------------
// Operation instance methods

// GetIsCancelled gets isCancelled
func (op *Operation) GetIsCancelled() bool {
	return op.isCancelled
}

// SetIsCancelled sets isCancelled
func (op *Operation) SetIsCancelled(value bool) {
	op.isCancelled = value
}

// GetRequestID gets the requestID
func (op *Operation) GetRequestID() int64 {
	return op.requestID
}

// SetRequestID sets the requestID
func (op *Operation) SetRequestID(value int64) {
	op.requestID = value
}

// GetRequest gets the request
func (op *Operation) GetRequest() messages.IProxyRequest {
	return op.request
}

// SetRequest sets the request
func (op *Operation) SetRequest(value messages.IProxyRequest) {
	op.request = value
}

// GetFuture gets a Operation's workflow.Future
//
// returns workflow.Future -> a cadence workflow.Future
func (op *Operation) GetFuture() workflow.Future {
	return op.future
}

// SetFuture sets a Operation's workflow.Future
//
// param value workflow.Future -> a cadence workflow.Future to be
// set as a Operation's cadence workflow.Future
func (op *Operation) SetFuture(value workflow.Future) {
	op.future = value
}

// GetSettable gets a Operation's workflow.Settable
//
// returns workflow.Settable -> a cadence workflow.Settable
func (op *Operation) GetSettable() workflow.Settable {
	return op.settable
}

// SetSettable sets a Operation's workflow.Settable
//
// param value workflow.Settable -> a cadence workflow.Settable to be
// set as a Operation's cadence workflow.Settable
func (op *Operation) SetSettable(value workflow.Settable) {
	op.settable = value
}

// SetReply signals the awaiting task that a workflow reply message
// has been received
func (op *Operation) SetReply(reply messages.IProxyReply, result interface{}) error {
	if op.future == nil {
		return argumentNilError
	}

	settable := op.GetSettable()
	if err := reply.GetError(); err != nil {
		settable.Set(nil, cadence.NewCustomError(err.ToString()))
	} else {
		settable.Set(result, nil)
	}

	return nil
}

// SetError signals the awaiting task that it should fails with an
// error
func (op *Operation) SetError(value error) error {
	if op.future == nil {
		return argumentNilError
	}

	settable := op.GetSettable()
	settable.Set(nil, cadence.NewCustomError(value.Error()))

	return nil
}

// SetCancelled signals the awaiting task that the Operation has
// been canceled
func (op *Operation) SetCancelled() {
	op.isCancelled = true
}

// GetChannel gets the Operation channel
func (op *Operation) GetChannel() chan interface{} {
	return op.channel
}

// SetChannel sets the Operation channel
func (op *Operation) SetChannel(value chan interface{}) {
	op.channel = value
}

// SendChannel sends an interface{} value over the
// Operation's channel
func (op *Operation) SendChannel(reply messages.IProxyReply, result interface{}) error {
	defer close(op.channel)
	if op.channel == nil {
		return argumentNilError
	}

	if err := reply.GetError(); err != nil {
		op.channel <- errors.New(err.ToString())
	} else {
		op.channel <- result
	}

	return nil
}

//----------------------------------------------------------------------------
// operationsMap instance methods

// Add adds a new Operation and its corresponding requestID into
// the operationsMap.  This method is thread-safe.
//
// param requestID int64 -> the requestID of the request sent to
// the Neon.Cadence lib client.  This will be the mapped key
//
// param value *Operation -> pointer to Operation to be set in the map.
// This will be the mapped value
//
// returns int64 -> requestID of the request being added
// in the Operation at the specified requestID
func (opMap *operationsMap) Add(requestID int64, value *Operation) int64 {
	opMap.Store(requestID, value)
	return requestID
}

// Remove removes key/value entry from the operationsMap at the specified
// requestID.  This is a thread-safe method.
//
// param requestID int64 -> the requestID of the request sent to
// the Neon.Cadence lib client.  This will be the mapped key
//
// returns int64 -> requestID of the request being removed in the
// Operation at the specified requestID
func (opMap *operationsMap) Remove(requestID int64) int64 {
	opMap.Delete(requestID)
	return requestID
}

// Get gets a Operation from the operationsMap at the specified
// requestID.  This method is thread-safe.
//
// param requestID int64 -> the requestID of the request sent to
// the Neon.Cadence lib client.  This will be the mapped key
//
// returns *Operation -> pointer to Operation at the specified requestID
// in the map.
func (opMap *operationsMap) Get(requestID int64) *Operation {
	if v, ok := opMap.Load(requestID); ok {
		if _v, _ok := v.(*Operation); _ok {
			return _v
		}
	}

	return nil
}