package main

import (
	"encoding/json"
	"errors"
	"fmt"
	"os"
	"os/exec"

	"github.com/Blackblock-inc/Cli-Proxy-API-Desktop/service/internal/shim"
)

func main() {
	if err := run(os.Args[1:]); err != nil {
		var exitError *exec.ExitError
		if errors.As(err, &exitError) {
			os.Exit(exitError.ExitCode())
		}

		fmt.Fprintln(os.Stderr, err)
		os.Exit(1)
	}
}

func run(args []string) error {
	runner := shim.NewRunner()

	if len(args) > 0 && (args[0] == "inspect" || args[0] == "--inspect") {
		resolution, err := runner.Inspect()
		if err != nil {
			return err
		}

		payload, err := json.MarshalIndent(resolution, "", "  ")
		if err != nil {
			return err
		}

		fmt.Println(string(payload))
		return nil
	}

	return runner.Run(args)
}
