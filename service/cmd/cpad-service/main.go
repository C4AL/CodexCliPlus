package main

import (
	"context"
	"encoding/json"
	"fmt"
	"os"

	"github.com/Blackblock-inc/Cli-Proxy-API-Desktop/service/internal/product"
	"github.com/Blackblock-inc/Cli-Proxy-API-Desktop/service/internal/service"
	"golang.org/x/sys/windows/svc"
)

func main() {
	if err := run(os.Args[1:]); err != nil {
		fmt.Fprintln(os.Stderr, err)
		os.Exit(1)
	}
}

func run(args []string) error {
	if len(args) > 0 {
		switch args[0] {
		case "layout":
			payload, err := json.MarshalIndent(product.NewLayout(), "", "  ")
			if err != nil {
				return err
			}

			fmt.Println(string(payload))
			return nil
		case "status":
			snapshot, err := service.LoadHostSnapshot()
			if err != nil {
				return err
			}

			payload, err := json.MarshalIndent(snapshot, "", "  ")
			if err != nil {
				return err
			}

			fmt.Println(string(payload))
			return nil
		case "codex-mode":
			if len(args) == 1 || args[1] == "status" || args[1] == "get" {
				resolution, err := service.GetCodexShimStatus()
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

			resolution, err := service.SetCodexMode(args[1])
			if err != nil {
				return err
			}

			payload, err := json.MarshalIndent(resolution, "", "  ")
			if err != nil {
				return err
			}

			fmt.Println(string(payload))
			return nil
		case "cpa-runtime":
			action := "status"
			if len(args) > 1 {
				action = args[1]
			}

			var (
				status any
				err    error
			)

			switch action {
			case "status", "get":
				status, err = service.InspectCPARuntime()
			case "build":
				status, err = service.BuildCPARuntime()
			case "start":
				status, err = service.StartCPARuntime()
			case "stop":
				status, err = service.StopCPARuntime()
			default:
				return fmt.Errorf("unknown cpa-runtime action: %s", action)
			}
			if err != nil {
				return err
			}

			payload, err := json.MarshalIndent(status, "", "  ")
			if err != nil {
				return err
			}

			fmt.Println(string(payload))
			return nil
		case "plugin-market":
			action := "status"
			if len(args) > 1 {
				action = args[1]
			}

			var (
				status any
				err    error
			)

			switch action {
			case "status", "get":
				status, err = service.InspectPluginMarket()
			case "refresh":
				status, err = service.RefreshPluginMarket()
			case "install":
				if len(args) < 3 {
					return fmt.Errorf("plugin-market install requires a plugin id")
				}
				status, err = service.InstallManagedPlugin(args[2])
			case "update":
				if len(args) < 3 {
					return fmt.Errorf("plugin-market update requires a plugin id")
				}
				status, err = service.UpdateManagedPlugin(args[2])
			case "enable":
				if len(args) < 3 {
					return fmt.Errorf("plugin-market enable requires a plugin id")
				}
				status, err = service.EnableManagedPlugin(args[2])
			case "disable":
				if len(args) < 3 {
					return fmt.Errorf("plugin-market disable requires a plugin id")
				}
				status, err = service.DisableManagedPlugin(args[2])
			case "diagnose":
				if len(args) < 3 {
					return fmt.Errorf("plugin-market diagnose requires a plugin id")
				}
				status, err = service.DiagnoseManagedPlugin(args[2])
			default:
				return fmt.Errorf("unknown plugin-market action: %s", action)
			}
			if err != nil {
				return err
			}

			payload, err := json.MarshalIndent(status, "", "  ")
			if err != nil {
				return err
			}

			fmt.Println(string(payload))
			return nil
		case "update-center":
			action := "status"
			if len(args) > 1 {
				action = args[1]
			}

			var (
				status any
				err    error
			)

			switch action {
			case "status", "get":
				status, err = service.InspectUpdateCenter()
			case "check", "refresh":
				status, err = service.CheckUpdateCenter()
			default:
				return fmt.Errorf("unknown update-center action: %s", action)
			}
			if err != nil {
				return err
			}

			payload, err := json.MarshalIndent(status, "", "  ")
			if err != nil {
				return err
			}

			fmt.Println(string(payload))
			return nil
		case "install":
			explicitBinaryPath := ""
			if len(args) > 1 {
				explicitBinaryPath = args[1]
			}

			return service.InstallService(explicitBinaryPath)
		case "remove":
			return service.RemoveService()
		case "start":
			return service.StartService()
		case "stop":
			return service.StopService()
		case "run":
			return service.NewRunner().RunInteractive(context.Background())
		case "service":
			return service.RunWindowsService()
		default:
			return fmt.Errorf("unknown command: %s", args[0])
		}
	}

	interactive, err := svc.IsAnInteractiveSession()
	if err == nil && !interactive {
		return service.RunWindowsService()
	}

	return service.NewRunner().RunInteractive(context.Background())
}
