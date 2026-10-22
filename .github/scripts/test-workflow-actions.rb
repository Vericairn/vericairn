# SPDX-License-Identifier: Apache-2.0
# Copyright (c) 2026 Jason Furr

require 'tmpdir'
require_relative 'check-workflow-actions'

pin = 'actions/checkout@' + 'a' * 40
Dir.mktmpdir('workflow-actions') do |directory|
  File.write(File.join(directory, 'actions.allow'), "# Approved exact revisions\n#{pin}\n")
  path = File.join(directory, 'test.yaml')
  cases = {
    'block step' => ["jobs:\n  test:\n    steps:\n      - uses: #{pin}\n", true],
    'flow and quoted key' => ["jobs: {test: {steps: [{'uses': '#{pin}'}]}}\n", true],
    'folded value' => ["jobs:\n  test:\n    uses: >-\n      #{pin}\n", true],
    'script is data' => ["jobs:\n  test:\n    steps:\n      - run: |\n          echo 'uses: unapproved/action@v1'\n# uses: ignored@v1\n", true],
    'unlisted revision' => ["jobs: {test: {uses: 'actions/checkout@#{'b' * 40}'}}", false],
    'unlisted action' => ["jobs: {test: {uses: 'other/action@#{'a' * 40}'}}", false],
    'version tag' => ["jobs: {test: {uses: actions/checkout@v5}}", false],
    'short hash' => ["jobs: {test: {uses: actions/checkout@aaaaaaa}}", false],
    'expression' => ['jobs: {test: {uses: "${{ inputs.action }}"}}', false],
    'local action' => ['jobs: {test: {uses: ./local}}', false],
    'reusable workflow beside it' => ['jobs: {test: {uses: ./.github/workflows/test.yaml}}', true],
    'missing reusable workflow' => ['jobs: {test: {uses: ./.github/workflows/other.yml}}', false],
    'reusable workflow elsewhere' => ['jobs: {test: {uses: ./.github/workflows/../test.yaml}}', false],
    'remote reusable workflow by tag' => ['jobs: {test: {uses: other/repo/.github/workflows/x.yml@v1}}', false],
    'container action' => ['jobs: {test: {uses: "docker://image:latest"}}', false],
    'alias value' => ["pin: &pin #{pin}\njobs: {test: {uses: *pin}}", false],
    'hidden unlisted anchor' => ['steps: &steps [{uses: unapproved/action@v1}]' + "\njobs: {test: {steps: *steps}}", false],
    'duplicate uses key' => ["jobs: {test: {uses: '#{pin}', uses: unapproved/action@v1}}", false],
    'invalid YAML' => ['jobs: [', false],
  }
  cases.each do |name, (yaml, passes)|
    File.write(path, yaml)
    errors = WorkflowActions.check(directory)
    raise "#{name}: expected #{passes ? 'pass' : 'failure'}, got #{errors.inspect}" unless errors.empty? == passes
    puts "PASS #{name}"
  end
  File.write(path, "jobs: {test: {uses: '#{pin}'}}")
  ['other/action', 'actions-lookalike/checkout', 'NuGet/other', 'NuGet/login/other'].each do |action|
    File.write(File.join(directory, 'actions.allow'), "#{pin}\n#{action}@#{'a' * 40}\n")
    raise "Unpermitted owner passed: #{action}" if WorkflowActions.check(directory).empty?
  end
  File.write(File.join(directory, 'actions.allow'), "#{pin}\ngithub/codeql-action/init@#{'a' * 40}\nNuGet/login@#{'b' * 40}\n")
  raise 'Permitted owners failed' unless WorkflowActions.check(directory).empty?
  File.write(File.join(directory, 'actions.allow'), "actions/checkout@v5\n")
  raise 'Unpinned allow-list entry passed' if WorkflowActions.check(directory).empty?
  File.delete(File.join(directory, 'actions.allow'))
  raise 'Missing allow-list passed' if WorkflowActions.check(directory).empty?
end

errors = WorkflowActions.check(File.expand_path('../workflows', __dir__))
raise errors.join("\n") unless errors.empty?
puts 'PASS repository workflows'
