package api

import (
	"net"
	"net/http"
	"strings"

	"github.com/gin-gonic/gin"
)

func loopbackOnlyMiddleware() gin.HandlerFunc {
	return func(c *gin.Context) {
		host := strings.TrimSpace(c.Request.RemoteAddr)
		if parsedHost, _, err := net.SplitHostPort(host); err == nil {
			host = parsedHost
		}

		ip := net.ParseIP(host)
		if ip == nil || !ip.IsLoopback() {
			c.AbortWithStatus(http.StatusForbidden)
			return
		}

		c.Next()
	}
}
