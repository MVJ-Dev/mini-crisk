# Toy credit-scoring model exposed as a REST API with plumber.
# The formula is NOT a real model: it only gives the .NET app something deterministic to call.

API_KEY <- Sys.getenv("SCORING_API_KEY", unset = "")

#* @filter auth
function(req, res) {
  # /health is open (load balancer checks); everything else needs the X-Api-Key header when a key is configured
  if (req$PATH_INFO == "/health" || API_KEY == "") {
    return(plumber::forward())
  }
  if (!identical(req$HTTP_X_API_KEY, API_KEY)) {
    res$status <- 401
    return(list(error = "invalid api key"))
  }
  plumber::forward()
}

#* Liveness
#* @get /health
#* @serializer unboxedJSON
function() {
  list(status = "ok", r_version = R.version.string)
}

#* Score a credit request
#* @post /score
#* @serializer unboxedJSON
function(req) {
  body <- jsonlite::fromJSON(req$postBody)
  amount <- as.numeric(body$Amount)
  age    <- as.numeric(body$CompanyAgeYears)
  prior  <- as.numeric(body$PriorDefaults)

  # Higher amount and prior defaults lower the score; company age raises it (bounded 0-100)
  raw <- 70 - 8 * log10(max(amount, 1) / 1000) + 1.5 * min(age, 20) - 12 * prior
  score <- round(min(max(raw, 0), 100), 3)

  list(Score = score, ModelVersion = "r-toy-1.0")
}
