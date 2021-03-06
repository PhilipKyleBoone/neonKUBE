// Copyright (c) 2019 Uber Technologies, Inc.
//
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in
// all copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
// THE SOFTWARE.

package main

import (
	"bytes"
	"flag"
	"fmt"
	"io/ioutil"
	"log"
	"os"
	"path/filepath"
	"strings"
	"time"
)

const (
	// how many lines to check for an existing copyright
	// this logic is not great and we should probably do something else
	// but this was copied from the python script
	copyrightLineLimit = 5
	headerPrefix       = "// Copyright (c)"
	headerFmtString    = headerPrefix + " %d Uber Technologies, Inc."
	licenseFmtString   = headerFmtString + `
//
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in
// all copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
// THE SOFTWARE.`
)

var (
	flagDryRun = flag.Bool("dry", false, "Do not edit files and just print out what files would be edited")

	lineSkipPrefixes = []string{
		"// Code generated by",
		"// @generated",
	}
)

func main() {
	log.SetFlags(0)
	log.SetOutput(os.Stdout)
	log.SetPrefix("")
	if err := do(); err != nil {
		log.Fatal(err)
	}
}

func do() error {
	flag.Parse()
	if len(flag.Args()) < 1 {
		return fmt.Errorf("usage: %s GO_FILES", os.Args[0])
	}

	return updateFiles(flag.Args(), time.Now().UTC().Year(), *flagDryRun)
}

func updateFiles(filePaths []string, year int, dryRun bool) error {
	if err := checkFilePaths(filePaths); err != nil {
		return err
	}
	for _, filePath := range filePaths {
		if err := updateFile(filePath, year, dryRun); err != nil {
			return err
		}
	}
	return nil
}

func checkFilePaths(filePaths []string) error {
	for _, filePath := range filePaths {
		if filepath.Ext(filePath) != ".go" {
			return fmt.Errorf("%s is not a go file", filePath)
		}
	}
	return nil
}

func updateFile(filePath string, year int, dryRun bool) error {
	data, err := ioutil.ReadFile(filePath)
	if err != nil {
		return err
	}
	newData := updateData(data, year)
	if !bytes.Equal(data, newData) {
		if dryRun {
			log.Print(filePath)
			return nil
		}
		// we could do something more complicated so that we do not
		// need to pass 0644 as the file mode, but in this case it should
		// never actually be used to create a file since we know the file
		// already exists, and it's easier to use the ReadFile/WriteFile
		// logic as it is right now, and since this is just a generation
		// program, this should be acceptable
		return ioutil.WriteFile(filePath, newData, 0644)
	}
	return nil
}

func updateData(data []byte, year int) []byte {
	return []byte(strings.Join(updateLines(strings.Split(string(data), "\n"), year), "\n"))
}

// a value in the returned slice may contain newlines itself
func updateLines(lines []string, year int) []string {
	for i, line := range lines {
		if i >= copyrightLineLimit {
			break
		}
		if strings.HasPrefix(line, headerPrefix) {
			lines[i] = headerString(year)
			return lines
		}
	}
	return addToLines(lines, year)
}

// a value in the returned slice may contain newlines itself
func addToLines(lines []string, year int) []string {
	i := 0
	for len(lines) > i && lineContainsSkipPrefix(lines[i]) {
		i++
		// skip comments under the generated line too
		for strings.HasPrefix(lines[i], "//") {
			i++
		}
	}
	if i == 0 {
		return append([]string{licenseString(year), ""}, lines...)
	}
	return append(lines[0:i], append([]string{"", licenseString(year)}, lines[i:]...)...)
}

func lineContainsSkipPrefix(line string) bool {
	for _, skipPrefix := range lineSkipPrefixes {
		if strings.HasPrefix(line, skipPrefix) {
			return true
		}
	}
	return false
}

func headerString(year int) string {
	return fmt.Sprintf(headerFmtString, year)
}

func licenseString(year int) string {
	return fmt.Sprintf(licenseFmtString, year)
}
