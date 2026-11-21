# Starts the scoring API on port 8000 (used by startup.ps1 on Azure; by a service/task on EC2).
# Install once: install.packages(c("plumber", "jsonlite"))
args <- commandArgs(trailingOnly = FALSE)
file_arg <- sub("^--file=", "", args[grep("^--file=", args)])
setwd(if (length(file_arg) > 0) dirname(normalizePath(file_arg)) else getwd())

pr <- plumber::plumb("plumber.R")
pr$run(host = "0.0.0.0", port = 8000)
