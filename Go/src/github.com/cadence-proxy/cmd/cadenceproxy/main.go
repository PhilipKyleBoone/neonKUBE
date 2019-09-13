//-----------------------------------------------------------------------------
// FILE:		main.go
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

package main

import (
	"flag"
	"net/http"
	"os"

	"go.uber.org/zap"
	"go.uber.org/zap/zapcore"

	"github.com/cadence-proxy/internal"
	"github.com/cadence-proxy/internal/endpoints"
	"github.com/cadence-proxy/internal/server"
)

var (

	// variables to put command line args in
	address   string
	debugMode bool

	// debugPrelaunched INTERNAL USE ONLY: Optionally indicates that the cadence-proxy will
	// already be running for debugging purposes.  When this is true, the
	// cadence-client be hardcoded to listen on 127.0.0.2:5001 and
	// the cadence-proxy will be assumed to be listening on 127.0.0.2:5000.
	// This defaults to false.
	debugPrelaunched = false
)

func main() {

	// define the flags and parse them
	flag.StringVar(&address, "listen", "127.0.0.2:5000", "Address for the Cadence Proxy Server to listen on")
	flag.BoolVar(&debugMode, "debug", true, "Set to debug mode")
	flag.Parse()

	// set debug
	logLevel := zapcore.InfoLevel
	if debugMode {
		internal.Debug = debugMode
		logLevel = zapcore.DebugLevel
	}
	internal.DebugPrelaunched = debugPrelaunched

	// set the initialization logger
	l := zap.New(
		zapcore.NewCore(
			endpoints.NewEncoder(),
			zapcore.Lock(os.Stdout),
			logLevel,
		), zap.AddCaller())
	defer l.Sync()

	// create the HTTP client used to
	// send messages back to the Neon.Cadence
	// client
	client := http.Client{
		Transport: http.DefaultTransport,
	}
	client.Transport.(*http.Transport).MaxIdleConnsPerHost = 10

	// create the instance, set the routes,
	// and start the server
	instance := server.NewInstance(address, l)

	// set server instance and
	// logger for endpoints
	// set HTTPClient
	// setup the routes
	endpoints.Logger = l.Named("init         ")
	endpoints.Instance = instance
	endpoints.HttpClient = client
	endpoints.SetupRoutes(instance.Router)

	// start the server
	instance.Start()
}
