// The MIT License (MIT)
//
// Copyright (c) 2020 Temporal Technologies, Inc.
//
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in all
// copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
// SOFTWARE.

// Code generated by protoc-gen-gogo. DO NOT EDIT.
// source: query/enum.proto

package query

import (
	fmt "fmt"
	proto "github.com/gogo/protobuf/proto"
	math "math"
)

// Reference imports to suppress errors if they are not otherwise used.
var _ = proto.Marshal
var _ = fmt.Errorf
var _ = math.Inf

// This is a compile-time assertion to ensure that this generated file
// is compatible with the proto package it is being compiled against.
// A compilation error at this line likely means your copy of the
// proto package needs to be updated.
const _ = proto.GoGoProtoPackageIsVersion3 // please upgrade the proto package

type QueryResultType int32

const (
	QueryResultType_Answered QueryResultType = 0
	QueryResultType_Failed   QueryResultType = 1
)

var QueryResultType_name = map[int32]string{
	0: "Answered",
	1: "Failed",
}

var QueryResultType_value = map[string]int32{
	"Answered": 0,
	"Failed":   1,
}

func (x QueryResultType) String() string {
	return proto.EnumName(QueryResultType_name, int32(x))
}

func (QueryResultType) EnumDescriptor() ([]byte, []int) {
	return fileDescriptor_4b1c9789806fe1e4, []int{0}
}

type QueryRejectCondition int32

const (
	// None indicates that query should not be rejected.
	QueryRejectCondition_None QueryRejectCondition = 0
	// NotOpen indicates that query should be rejected if workflow is not open.
	QueryRejectCondition_NotOpen QueryRejectCondition = 1
	// NotCompletedCleanly indicates that query should be rejected if workflow did not complete cleanly.
	QueryRejectCondition_NotCompletedCleanly QueryRejectCondition = 2
)

var QueryRejectCondition_name = map[int32]string{
	0: "None",
	1: "NotOpen",
	2: "NotCompletedCleanly",
}

var QueryRejectCondition_value = map[string]int32{
	"None":                0,
	"NotOpen":             1,
	"NotCompletedCleanly": 2,
}

func (x QueryRejectCondition) String() string {
	return proto.EnumName(QueryRejectCondition_name, int32(x))
}

func (QueryRejectCondition) EnumDescriptor() ([]byte, []int) {
	return fileDescriptor_4b1c9789806fe1e4, []int{1}
}

type QueryConsistencyLevel int32

const (
	// Eventual indicates that query should be eventually consistent.
	QueryConsistencyLevel_Eventual QueryConsistencyLevel = 0
	// Strong indicates that any events that came before query should be reflected in workflow state before running query.
	QueryConsistencyLevel_Strong QueryConsistencyLevel = 1
)

var QueryConsistencyLevel_name = map[int32]string{
	0: "Eventual",
	1: "Strong",
}

var QueryConsistencyLevel_value = map[string]int32{
	"Eventual": 0,
	"Strong":   1,
}

func (x QueryConsistencyLevel) String() string {
	return proto.EnumName(QueryConsistencyLevel_name, int32(x))
}

func (QueryConsistencyLevel) EnumDescriptor() ([]byte, []int) {
	return fileDescriptor_4b1c9789806fe1e4, []int{2}
}

func init() {
	proto.RegisterEnum("query.QueryResultType", QueryResultType_name, QueryResultType_value)
	proto.RegisterEnum("query.QueryRejectCondition", QueryRejectCondition_name, QueryRejectCondition_value)
	proto.RegisterEnum("query.QueryConsistencyLevel", QueryConsistencyLevel_name, QueryConsistencyLevel_value)
}

func init() { proto.RegisterFile("query/enum.proto", fileDescriptor_4b1c9789806fe1e4) }

var fileDescriptor_4b1c9789806fe1e4 = []byte{
	// 257 bytes of a gzipped FileDescriptorProto
	0x1f, 0x8b, 0x08, 0x00, 0x00, 0x00, 0x00, 0x00, 0x02, 0xff, 0x4c, 0x8f, 0xcf, 0x4a, 0xfb, 0x40,
	0x10, 0xc7, 0xb3, 0x3f, 0x7e, 0xd6, 0x32, 0x0a, 0x2e, 0xab, 0xd2, 0xdb, 0x5e, 0xbc, 0x55, 0x4c,
	0x10, 0x9f, 0x40, 0x83, 0x3d, 0x49, 0xfc, 0x7b, 0x10, 0x6f, 0xb1, 0x19, 0xca, 0xca, 0x66, 0x26,
	0x26, 0x93, 0x4a, 0xde, 0xc2, 0xc7, 0xf2, 0xd8, 0xa3, 0x47, 0x49, 0x5e, 0x44, 0xba, 0x55, 0xf0,
	0xf6, 0x9d, 0x99, 0x0f, 0xf3, 0xe5, 0x03, 0xfa, 0xb5, 0xc5, 0xba, 0x4b, 0x90, 0xda, 0x32, 0xae,
	0x6a, 0x16, 0x36, 0x5b, 0x61, 0x33, 0x3d, 0x86, 0xbd, 0xdb, 0x75, 0xb8, 0xc3, 0xa6, 0xf5, 0xf2,
	0xd0, 0x55, 0x68, 0x76, 0x61, 0x7c, 0x4e, 0xcd, 0x1b, 0xd6, 0x58, 0xe8, 0xc8, 0x00, 0x8c, 0x66,
	0xb9, 0xf3, 0x58, 0x68, 0x35, 0x9d, 0xc1, 0xc1, 0x0f, 0xfc, 0x82, 0x73, 0x49, 0x99, 0x0a, 0x27,
	0x8e, 0xc9, 0x8c, 0xe1, 0x7f, 0xc6, 0x84, 0x3a, 0x32, 0x3b, 0xb0, 0x9d, 0xb1, 0x5c, 0x57, 0x48,
	0x5a, 0x99, 0x09, 0xec, 0x67, 0x2c, 0x29, 0x97, 0x95, 0x47, 0xc1, 0x22, 0xf5, 0x98, 0x93, 0xef,
	0xf4, 0xbf, 0xe9, 0x29, 0x1c, 0x86, 0x3f, 0x29, 0x53, 0xe3, 0x1a, 0x41, 0x9a, 0x77, 0x57, 0xb8,
	0x44, 0xbf, 0xae, 0xbe, 0x5c, 0x22, 0x49, 0x9b, 0xfb, 0x4d, 0xf5, 0xbd, 0xd4, 0x4c, 0x0b, 0xad,
	0x2e, 0x1e, 0x3f, 0x7a, 0xab, 0x56, 0xbd, 0x55, 0x5f, 0xbd, 0x55, 0xef, 0x83, 0x8d, 0x56, 0x83,
	0x8d, 0x3e, 0x07, 0x1b, 0xc1, 0xc4, 0x71, 0x2c, 0x58, 0x56, 0x5c, 0xe7, 0x7e, 0xe3, 0x16, 0x07,
	0xb5, 0x1b, 0xf5, 0x74, 0xb4, 0xf8, 0x73, 0x72, 0x9c, 0xfc, 0xe6, 0x93, 0x80, 0x25, 0x01, 0x7b,
	0x1e, 0x85, 0xe1, 0xec, 0x3b, 0x00, 0x00, 0xff, 0xff, 0xb8, 0x08, 0x50, 0x79, 0x23, 0x01, 0x00,
	0x00,
}
