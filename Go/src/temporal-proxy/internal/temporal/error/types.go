//-----------------------------------------------------------------------------
// FILE:		types.go
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

package proxyerror

import "fmt"

// TemporalErrorType is an enumerated list of
// all of the temporal error types
type TemporalErrorType int

const (

	// Cancelled indicates that an operation was cancelled
	Cancelled TemporalErrorType = 0

	// Custom is a custom error
	Custom TemporalErrorType = 1

	// Generic is a generic error
	Generic TemporalErrorType = 2

	// Panic is a panic error
	Panic TemporalErrorType = 3

	// Terminated is a termination error
	Terminated TemporalErrorType = 4

	// Timeout is a timeout error
	Timeout TemporalErrorType = 5
)

func (t TemporalErrorType) String() string {
	return [...]string{
		"cancelled",
		"custom",
		"generic",
		"panic",
		"terminated",
		"timeout",
	}[t]
}

// ToTemporalError takes a string value and converts it into the corresponding
// TemporalErrorType
//
// param value string -> the string representation of a TemporalErrorType
//
// returns TemporalErrorType -> the corresponding TemporalErrorType
func ToTemporalError(value string) TemporalErrorType {
	var errType TemporalErrorType
	switch value {
	case "cancelled":
		errType = Cancelled
	case "custom":
		errType = Custom
	case "generic":
		errType = Generic
	case "panic":
		errType = Panic
	case "terminated":
		errType = Terminated
	case "timeout":
		errType = Timeout
	default:
		err := fmt.Errorf("unrecognized error type %s", value)
		panic(err)
	}

	return errType
}
